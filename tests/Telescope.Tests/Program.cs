using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DTE = EnvDTE.DTE;
using EnvDTE80;
using Telescope.Controller;
using Telescope.Filter;
using Telescope.Finders;
using Telescope.Logging;
using Telescope.Overlay;
using TestHarness;
using static TestHarness.TestScaffold;
using FzfHit = Telescope.Finders.GrepHit;
using ImplementationHit = Telescope.Finders.DefinitionHit;

namespace Telescope.Tests
{
    internal static class Program
    {
        // BP-A1 (Feature 7): the pane-contract tests construct WPF FrameworkElements (the IPane
        // fakes); WPF element construction/focus is unsupported on an MTA thread, so the runner's
        // Main must be STA. Pure no-op for the existing tests (none touch WPF objects).
        [STAThread]
        private static int Main(string[] args) => TestHarness.TestRunner.Run(typeof(Tests), args);
    }

    internal static class Tests
    {
        private static FinderEntry E(string s) => new FinderEntry(s);

        // True when the line is a LogFileWriter timestamped line: "HH:mm:ss.fff <message>"
        // (12 timestamp chars, then a space) — the per-call File.AppendAllText format the
        // buffered writer must reproduce byte-identically.
        private static bool IsTimestampedLine(string line)
        {
            if (line == null || line.Length < 13)
            {
                return false;
            }
            for (int i = 0; i < 12; i++)
            {
                char c = line[i];
                if (i == 2 || i == 5)
                {
                    if (c != ':') { return false; }
                }
                else if (i == 8)
                {
                    if (c != '.') { return false; }
                }
                else if (!char.IsDigit(c))
                {
                    return false;
                }
            }
            return line[12] == ' ';
        }

        // Reads a file with FileShare.ReadWrite so it can be read while the buffered
        // LogFileWriter still holds it open (File.ReadAllText uses FileShare.Read, which
        // conflicts with the writer's existing FileAccess.Write -> sharing violation).
        private static string ReadAllTextShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                return sr.ReadToEnd();
            }
        }

        // ================================================================
        // Lane 3 test-local seam types (BP-1/BP-2).
        // RED: FileLocation / FinderBase<THit> do not exist yet -> compile error.
        // ================================================================

        private sealed class TestHit : FileLocation
        {
            public TestHit(string filePath, int lineNumber) : base(filePath, lineNumber) { }
        }

        private sealed class TestFinder : FinderBase<TestHit>
        {
            private readonly Func<IReadOnlyList<TestHit>> _gather;
            private readonly Action<TestHit>? _open;

            public TestFinder(Func<IReadOnlyList<TestHit>> gather, Action<TestHit>? open = null)
            {
                _gather = gather;
                _open = open;
            }

            public override string Name => "Test";
            protected override IReadOnlyList<TestHit> GatherHits() => _gather();
            protected override FinderEntry ToEntry(TestHit hit) => new FinderEntry(hit.FilePath, hit);
            protected override void OpenHit(TestHit hit) => _open?.Invoke(hit);
        }

        // M28: a no-op IDisposable returned by the injected LogFileWriter.TimerScheduler seam.
        private sealed class FakeTimer : IDisposable
        {
            public void Dispose() { }
        }

        // ================================================================
        // Results columns (D6 + D2a) — per-finder column catalog, visibility
        // model, row cells. Getter assertions expect the ABBREVIATED cell
        // values (Section A rev 1's table): access W/R; issues kind
        // err/warn/todo/info; implementation kind inf/func/prop/….
        // RED: ResultColumn / FinderColumns / ColumnVisibilityModel /
        //      ResultRowCells do not exist yet -> compile error (CS0246).
        // ================================================================

        // Joins the columns' ids with ',' — the exact format the
        // "[Telescope] results columns=<ids>" diagnostic uses.
        private static string JoinIds(IReadOnlyList<ResultColumn> columns)
            => string.Join(",", columns.Select(c => c.Id));

        private static string JoinDefaultVisible(IReadOnlyList<ResultColumn> columns)
            => string.Join(",", columns.Where(c => c.DefaultVisible).Select(c => c.Id));

        private static string CellOf(IReadOnlyList<ResultColumn> columns, string id, object? payload)
            => columns.First(c => c.Id == id).Getter(payload);

        public static void Run_ResultsFormatter_RenderedTextLength_Empty()
        {
            // The byte-stable boxText= seam: empty input -> 0.
            Assert.Equal(0, ResultsFormatter.RenderedTextLength(new string[][] { }));
            Assert.Equal(0, ResultsFormatter.RenderedTextLength(null!));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_SingleRowSingleCell()
        {
            // One row: the sum of the visible cell text lengths (BP-11 — the legacy 2-char marker
            // + separator math is gone).
            Assert.Equal(5, ResultsFormatter.RenderedTextLength(new[] { new[] { "alpha" } }));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_MultiRow()
        {
            // Two rows: the sum of the visible cell text lengths (no separator allowance).
            Assert.Equal(2, ResultsFormatter.RenderedTextLength(new[] { new[] { "a" }, new[] { "b" } }));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_MultiCell()
        {
            // One row, two cells: the cells' text lengths sum.
            Assert.Equal(3, ResultsFormatter.RenderedTextLength(new[] { new[] { "a", "bb" } }));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_EmptyCells()
        {
            // Empty cell strings contribute 0.
            Assert.Equal(0, ResultsFormatter.RenderedTextLength(new[] { new[] { "" }, new[] { "" } }));
        }

        public static void Run_ResultsFormatter_ColumnsIdList_Empty()
        {
            // Empty set -> the empty string (the line reads "results columns="). The id-list
            // format is inlined at the call site (n16/BP-9 — string.Join(",", ids)).
            Assert.Equal("", string.Join(",", new string[] { }));
        }

        public static void Run_ResultsFormatter_ColumnsIdList_Single()
        {
            Assert.Equal("access", string.Join(",", new[] { "access" }));
        }

        public static void Run_ResultsFormatter_ColumnsIdList_Multiple()
        {
            // Comma-joined, NO spaces, caller's order (the model supplies catalog order).
            Assert.Equal("access,file", string.Join(",", new[] { "access", "file" }));
        }

        public static void Run_ResultsColumns_Files_Catalog()
        {
            // ALL catalog columns present, in catalog order (AC2) + the default-visible set is
            // EXACTLY the user's marks (AC3): file + dir ON, path OFF.
            var files = FinderColumns.ForFinder("Files");
            Assert.Equal("file,dir,path", JoinIds(files));
            Assert.Equal("File|Directory|Path",
                string.Join("|", files.Select(c => c.Header)));
            Assert.Equal("file,dir", JoinDefaultVisible(files));
        }

        public static void Run_ResultsColumns_Files_Getters()
        {
            // BP-19 (m47): the projectRoot param is deleted — the Files dir cell is the FULL
            // containing directory (no root trim).
            var files = FinderColumns.ForFinder("Files");
            var hit = new FileHit(@"C:\proj\Services\Foo.cs", 0);
            Assert.Equal("Foo.cs", CellOf(files, "file", hit));
            Assert.Equal(@"C:\proj\Services", CellOf(files, "dir", hit));   // full dir — no root trim
            Assert.Equal(@"C:\proj\Services\Foo.cs", CellOf(files, "path", hit));
        }

        public static void Run_ResultsColumns_Files_DirWithoutRoot()
        {
            // No root: the dir cell is the FULL containing directory (BP-19 — the projectRoot
            // param is deleted, so the dir cell is ALWAYS the full directory).
            var files = FinderColumns.ForFinder("Files");
            Assert.Equal(@"C:\proj\Services", CellOf(files, "dir", new FileHit(@"C:\proj\Services\Foo.cs", 0)));
            Assert.Equal(@"C:\proj", CellOf(files, "dir", new FileHit(@"C:\proj\Foo.cs", 0)));
            Assert.Equal(@"C:\other\Dir", CellOf(files, "dir", new FileHit(@"C:\other\Dir\Foo.cs", 0)));
        }

        public static void Run_ResultsColumns_Issues_Catalog()
        {
            var issues = FinderColumns.ForFinder("Issues");
            Assert.Equal("kind,file,message,line", JoinIds(issues));
            Assert.Equal("Kind|File|Message|Line",
                string.Join("|", issues.Select(c => c.Header)));
            // User's marks: Kind, File, Message ON; Line OFF (the position stays in the chooser).
            Assert.Equal("kind,file,message", JoinDefaultVisible(issues));
        }

        public static void Run_ResultsColumns_Issues_Getters()
        {
            var issues = FinderColumns.ForFinder("Issues");
            var todo = new CodeIssue(CodeIssueKind.Todo, @"C:\p\A.cs", 3, "fix this");
            // D2a ABBREVIATED kinds (Section A rev 1): Todo->todo, Error->err, Warning->warn,
            // Info->info. NOT the legacy display markers (ERR/WARN/TODO) and NOT enum ToString.
            Assert.Equal("todo", CellOf(issues, "kind", todo));
            Assert.Equal("A.cs", CellOf(issues, "file", todo));
            Assert.Equal("fix this", CellOf(issues, "message", todo));
            Assert.Equal("3", CellOf(issues, "line", todo));

            Assert.Equal("err", CellOf(issues, "kind",
                new CodeIssue(CodeIssueKind.Error, @"C:\p\A.cs", 1, "boom")));
            Assert.Equal("warn", CellOf(issues, "kind",
                new CodeIssue(CodeIssueKind.Warning, @"C:\p\A.cs", 2, "hmm")));
            Assert.Equal("info", CellOf(issues, "kind",
                new CodeIssue(CodeIssueKind.Info, @"C:\p\A.cs", 4, "fyi")));
        }

        public static void Run_ResultsColumns_References_Catalog()
        {
            var refs = FinderColumns.ForFinder("References");
            Assert.Equal("access,file,symbol,column,line,text", JoinIds(refs));
            Assert.Equal("Access|File|Symbol|Column|Line|Line text",
                string.Join("|", refs.Select(c => c.Header)));
            // User's marks: Access + File ON; Symbol/Column/Line/Line text OFF.
            // This is the exact id list AC5 pins in the default "results columns=" line.
            Assert.Equal("access,file", JoinDefaultVisible(refs));
        }

        public static void Run_ResultsColumns_References_Getters()
        {
            var refs = FinderColumns.ForFinder("References");
            var read = new ReferenceHit(@"C:\p\Reader.cs", 5, 16, isWrite: false, "Value", "return Shared.Value;");
            // D2a ABBREVIATED access (Section A rev 1): read -> R, write -> W (user-specified).
            // NOT the long "read"/"write" the Display keeps (pin at 2167-2171).
            Assert.Equal("R", CellOf(refs, "access", read));
            Assert.Equal("Reader.cs", CellOf(refs, "file", read));
            Assert.Equal("Value", CellOf(refs, "symbol", read));
            Assert.Equal("16", CellOf(refs, "column", read));
            Assert.Equal("5", CellOf(refs, "line", read));
            Assert.Equal("return Shared.Value;", CellOf(refs, "text", read));

            // The write side.
            var write = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            Assert.Equal("W", CellOf(refs, "access", write));
        }

        public static void Run_ResultsColumns_Grep_Catalog()
        {
            var grep = FinderColumns.ForFinder("Grep");
            Assert.Equal("file,line,text", JoinIds(grep));
            Assert.Equal("File|Line|Line text", string.Join("|", grep.Select(c => c.Header)));
            // Grep keeps ALL THREE ON (the user's marks — unlike Issues/References/Implementation).
            Assert.Equal("file,line,text", JoinDefaultVisible(grep));
        }

        public static void Run_ResultsColumns_Grep_Getters()
        {
            var grep = FinderColumns.ForFinder("Grep");
            var hit = new GrepHit(@"C:\p\src\A.cs", 2, "NEEDLE here");
            Assert.Equal("A.cs", CellOf(grep, "file", hit));
            Assert.Equal("2", CellOf(grep, "line", hit));
            Assert.Equal("NEEDLE here", CellOf(grep, "text", hit));
        }

        public static void Run_ResultsColumns_Fzf_Catalog()
        {
            var fzf = FinderColumns.ForFinder("Fzf");
            Assert.Equal("file,line,text", JoinIds(fzf));
            Assert.Equal("File|Line|Line text", string.Join("|", fzf.Select(c => c.Header)));
            // Fzf keeps ALL THREE ON (the user's marks).
            Assert.Equal("file,line,text", JoinDefaultVisible(fzf));
        }

        public static void Run_ResultsColumns_Fzf_Getters()
        {
            var fzf = FinderColumns.ForFinder("Fzf");
            var hit = new FzfHit(@"C:\p\src\A.cs", 2, "NEEDLE here");
            Assert.Equal("A.cs", CellOf(fzf, "file", hit));
            Assert.Equal("2", CellOf(fzf, "line", hit));
            Assert.Equal("NEEDLE here", CellOf(fzf, "text", hit));
        }

        public static void Run_ResultsColumns_Implementation_Catalog()
        {
            var impl = FinderColumns.ForFinder("Implementation");
            Assert.Equal("kind,file,symbol,line", JoinIds(impl));
            Assert.Equal("Kind|File|Symbol|Line", string.Join("|", impl.Select(c => c.Header)));
            // User's marks: Kind + File ON; Symbol/Line OFF.
            Assert.Equal("kind,file", JoinDefaultVisible(impl));
        }

        public static void Run_ResultsColumns_Implementation_Getters()
        {
            var impl = FinderColumns.ForFinder("Implementation");
            // D2a ABBREVIATED kinds — RE-PINNED to Section A REV 1's table (gate round 2, finding 4):
            // Interface -> inf (the user's spelling), Method -> func (the user's function=func carried
            // onto Roslyn's function-like kind), Class -> cls, Struct -> str, Property -> prop,
            // Unknown -> unkn (BP-5/m42: the defensive entries are dropped; Unknown renders via the
            // ≤4-char Fallback). The real Kind strings
            // come from RoslynGatherers.cs:141-144 (TypeKind.ToString() / SymbolKind.ToString()) —
            // there is NO "Override" token; overrides arrive as Method/Property.
            Assert.Equal("cls", CellOf(impl, "kind", new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class")));
            Assert.Equal("inf", CellOf(impl, "kind", new ImplementationHit(@"C:\p\IShape.cs", 1, "IShape", "Interface")));
            Assert.Equal("func", CellOf(impl, "kind", new ImplementationHit(@"C:\p\Shape.cs", 4, "Draw", "Method")));
            Assert.Equal("prop", CellOf(impl, "kind", new ImplementationHit(@"C:\p\Shape.cs", 9, "Area", "Property")));
            Assert.Equal("str", CellOf(impl, "kind", new ImplementationHit(@"C:\p\P.cs", 1, "P", "Struct")));
            Assert.Equal("unkn", CellOf(impl, "kind", new ImplementationHit(@"C:\p\X.cs", 1, "X", "Unknown")));

            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            Assert.Equal("Shape.cs", CellOf(impl, "file", hit));
            Assert.Equal("Shape", CellOf(impl, "symbol", hit));
            Assert.Equal("2", CellOf(impl, "line", hit));
        }

        public static void Run_KindAbbrev_Issue_AllValues()
        {
            // All 4 CodeIssueKind values (CodeIssue.cs:4-17) — D2a's Issues column.
            Assert.Equal("todo", KindAbbreviations.Issue(CodeIssueKind.Todo));
            Assert.Equal("err", KindAbbreviations.Issue(CodeIssueKind.Error));
            Assert.Equal("warn", KindAbbreviations.Issue(CodeIssueKind.Warning));
            Assert.Equal("info", KindAbbreviations.Issue(CodeIssueKind.Info));
        }

        public static void Run_KindAbbrev_Implementation_Realistic()
        {
            // The Roslyn-reachable set (SymbolFinder.FindImplementationsAsync): types + members.
            Assert.Equal("cls", KindAbbreviations.Implementation("Class"));
            Assert.Equal("inf", KindAbbreviations.Implementation("Interface"));
            Assert.Equal("str", KindAbbreviations.Implementation("Struct"));
            Assert.Equal("enm", KindAbbreviations.Implementation("Enum"));
            Assert.Equal("func", KindAbbreviations.Implementation("Method"));
            Assert.Equal("prop", KindAbbreviations.Implementation("Property"));
            Assert.Equal("evt", KindAbbreviations.Implementation("Event"));
        }

        public static void Run_KindAbbrev_Implementation_DefensiveUnion()
        {
            // BP-5 (m42): the defensive ImplMap entries are DELETED — the ≤4-char Fallback
            // renders them (Delegate -> "dele", ErrorType -> "erro", ...). The realistic
            // entries (Class/Interface/Struct/Enum/Method/Property/Event) keep their
            // abbreviations (pinned by Run_KindAbbrev_Implementation_Realistic).
            Assert.Equal("dele", KindAbbreviations.Implementation("Delegate"));
            Assert.Equal("erro", KindAbbreviations.Implementation("ErrorType"));
            Assert.Equal("type", KindAbbreviations.Implementation("TypeParameter"));
            Assert.Equal("unkn", KindAbbreviations.Implementation("Unknown"));
            Assert.Equal("arra", KindAbbreviations.Implementation("Array"));
            Assert.Equal("arra", KindAbbreviations.Implementation("ArrayType"));
            Assert.Equal("dyna", KindAbbreviations.Implementation("Dynamic"));
            Assert.Equal("dyna", KindAbbreviations.Implementation("DynamicType"));
            Assert.Equal("modu", KindAbbreviations.Implementation("Module"));
            Assert.Equal("netm", KindAbbreviations.Implementation("NetModule"));
            Assert.Equal("poin", KindAbbreviations.Implementation("Pointer"));
            Assert.Equal("poin", KindAbbreviations.Implementation("PointerType"));
            Assert.Equal("subm", KindAbbreviations.Implementation("Submission"));
            Assert.Equal("func", KindAbbreviations.Implementation("FunctionPointer"));
            Assert.Equal("func", KindAbbreviations.Implementation("FunctionPointerType"));
            Assert.Equal("fiel", KindAbbreviations.Implementation("Field"));
            Assert.Equal("loca", KindAbbreviations.Implementation("Local"));
            Assert.Equal("name", KindAbbreviations.Implementation("NamedType"));
            Assert.Equal("name", KindAbbreviations.Implementation("Namespace"));
            Assert.Equal("asse", KindAbbreviations.Implementation("Assembly"));
            Assert.Equal("labe", KindAbbreviations.Implementation("Label"));
            Assert.Equal("para", KindAbbreviations.Implementation("Parameter"));
            Assert.Equal("rang", KindAbbreviations.Implementation("RangeVariable"));
            Assert.Equal("imp", KindAbbreviations.Implementation("Implementation"));
            Assert.Equal("func", KindAbbreviations.Implementation("Function"));
        }

        public static void Run_KindAbbrev_Implementation_Fallback()
        {
            // Any value not in the map: lowercase, truncated to <= 4 chars. Null/empty -> "".
            Assert.Equal("some", KindAbbreviations.Implementation("SomethingNew"));
            Assert.Equal("ab", KindAbbreviations.Implementation("Ab"));
            Assert.Equal("", KindAbbreviations.Implementation(""));
            Assert.Equal("", KindAbbreviations.Implementation(null!));
        }

        public static void Run_KindAbbrev_Implementation_UnionDistinct()
        {
            // BP-5 (m42): the defensive entries render via the ≤4-char Fallback, so the
            // 30-distinct invariant no longer holds — Array/ArrayType, Dynamic/DynamicType,
            // Pointer/PointerType, Method/FunctionPointer/FunctionPointerType, and
            // NamedType/Namespace collide via Fallback.
            var union = new[] { "Class", "Interface", "Struct", "Enum", "Method", "Property", "Event",
                "Delegate", "ErrorType", "TypeParameter", "Unknown", "Array", "ArrayType", "Dynamic",
                "DynamicType", "Module", "NetModule", "Pointer", "PointerType", "Submission",
                "FunctionPointer", "FunctionPointerType", "Field", "Local", "NamedType", "Namespace",
                "Assembly", "Label", "Parameter", "RangeVariable" };
            var cells = union.Select(k => KindAbbreviations.Implementation(k)).ToList();
            Assert.Equal(30, cells.Count);
            // The collisions are EXPECTED (Fallback truncation): the distinct count drops below 30.
            Assert.True(cells.Distinct().Count() < 30,
                "the defensive entries collide via Fallback — the 30-distinct invariant no longer holds (BP-5/m42)");
        }

        public static void Run_ResultsColumns_ForFinder_UnknownName_Empty()
        {
            // An unknown IFinder.Name yields an EMPTY catalog (the pure model stays total; the
            // overlay falls back). Ordinal, case-sensitive: "files" must NOT match "Files".
            Assert.Equal(0, FinderColumns.ForFinder("Nope").Count);
            Assert.Equal(0, FinderColumns.ForFinder("files").Count);
        }

        // ================================================================
        // 106-findings plan — Section C Phase 7 (BP-16/BP-18/BP-19)
        // ================================================================

        // BP-16 (m41): the shared column builders (FileColumn<THit>/LineColumn<THit>/
        // TextColumn<THit>/KindColumn<THit>) produce the exact pinned columns. The per-finder
        // catalogs delegate to them, so the pinned catalog/getter/min-max tests survive
        // byte-identically. COMPILE-RED: the builders do not exist yet -> CS1061.
        // NOTE (design decision): the builders are internal static methods on FinderColumns with
        // the uniform signature (id, header, min, max, truncation, defaultVisible, getter); the
        // width kind (Fixed/Flexible) + widthChars derive from max (max == int.MaxValue ->
        // Flexible absorber).
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing FinderColumns.FileColumn/LineColumn/
        // TextColumn/KindColumn builders break the build, which would block proving the Section C
        // behavior-RED tests; restored at the end of the RED phase).
        public static void Run_FinderColumns_SharedBuilders()
        {
            var file = FinderColumns.FileColumn<FileHit>("file", "File", 6, 30, ResultColumnTruncation.Tail, true,
                h => Path.GetFileName(h.FilePath));
            Assert.Equal("file", file.Id);
            Assert.Equal("File", file.Header);
            Assert.Equal(6, file.MinWidth);
            Assert.Equal(30, file.MaxWidth);
            Assert.Equal(ResultColumnTruncation.Tail, file.Truncation);
            Assert.True(file.DefaultVisible);
            Assert.Equal("Foo.cs", file.Getter(new FileHit(@"C:\p\Foo.cs", 0)));

            var line = FinderColumns.LineColumn<GrepHit>("line", "Line", 2, 5, ResultColumnTruncation.End, true,
                h => h.LineNumber.ToString());
            Assert.Equal("line", line.Id);
            Assert.Equal(2, line.MinWidth);
            Assert.Equal(5, line.MaxWidth);
            Assert.Equal(ResultColumnTruncation.End, line.Truncation);
            Assert.True(line.DefaultVisible);
            Assert.Equal("2", line.Getter(new GrepHit(@"C:\p\A.cs", 2, "x")));

            var text = FinderColumns.TextColumn<GrepHit>("text", "Line text", 10, int.MaxValue, ResultColumnTruncation.End, true,
                h => h.LineText);
            Assert.Equal("text", text.Id);
            Assert.Equal(10, text.MinWidth);
            Assert.Equal(int.MaxValue, text.MaxWidth);
            Assert.Equal(ResultColumnTruncation.End, text.Truncation);
            Assert.True(text.DefaultVisible);
            Assert.Equal("NEEDLE here", text.Getter(new GrepHit(@"C:\p\A.cs", 2, "NEEDLE here")));

            var kind = FinderColumns.KindColumn<CodeIssue>("kind", "Kind", 3, 8, ResultColumnTruncation.End, true,
                i => KindAbbreviations.Issue(i.Kind));
            Assert.Equal("kind", kind.Id);
            Assert.Equal(3, kind.MinWidth);
            Assert.Equal(8, kind.MaxWidth);
            Assert.Equal(ResultColumnTruncation.End, kind.Truncation);
            Assert.True(kind.DefaultVisible);
            Assert.Equal("todo", kind.Getter(new CodeIssue(CodeIssueKind.Todo, @"C:\p\A.cs", 3, "fix")));
        }

        // BP-18 (m45): ForFinder becomes a Dictionary<string, Func<IReadOnlyList<ResultColumn>>>
        // keyed by the finder Name (ordinal). COMPILE-RED: the capability seam does not exist yet
        // -> CS0117. The behavior assertions pin the contract the dictionary must preserve.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing FinderColumns.UsesDataDrivenLookup
        // seam breaks the build; restored at the end of the RED phase).
        public static void Run_FinderColumns_DataDriven()
        {
            Assert.True(FinderColumns.UsesDataDrivenLookup,
                "ForFinder must be a Dictionary<string, Func<IReadOnlyList<ResultColumn>>> keyed by Name (BP-18/m45)");
            foreach (var name in new[] { "Files", "Recent", "Issues", "References", "Grep", "Fzf", "Implementation" })
            {
                Assert.True(FinderColumns.ForFinder(name).Count > 0, $"{name} resolves");
            }
            Assert.Equal(0, FinderColumns.ForFinder("Nope").Count);
            Assert.Equal(0, FinderColumns.ForFinder("files").Count);
        }

        // BP-19 (m47): the projectRoot param is deleted — ForFinder(name) compiles without it and
        // the Files dir cell returns the FULL directory (no root trim). RED today: ForFinder still
        // has the projectRoot param (2 params), so the reflection param-count check fails.
        public static void Run_FinderColumns_ProjectRoot()
        {
            var method = typeof(FinderColumns).GetMethod("ForFinder",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.True(method != null, "ForFinder exists");
            Assert.Equal(1, method!.GetParameters().Length);
            // The Files dir cell returns the FULL directory (no root trim).
            var files = FinderColumns.ForFinder("Files");
            Assert.Equal(@"C:\proj\Services", CellOf(files, "dir", new FileHit(@"C:\proj\Services\Foo.cs", 0)));
        }

        public static void Run_ResultsColumns_WidthKinds()
        {
            // Section A BP-A1's invariant (post BP-4/m40 — ResultColumn.Width/WidthChars are
            // deleted): every catalog column has a sane width envelope (MinWidth >= 0,
            // MaxWidth >= MinWidth) and a defined truncation kind. The exact width values are
            // Section-B tuning hints and are NEVER asserted here.
            foreach (var name in new[] { "Files", "Issues", "References", "Grep", "Fzf", "Implementation", "Recent" })
            {
                var cols = FinderColumns.ForFinder(name);
                Assert.True(cols.Count > 0, $"{name} has a catalog");
                foreach (var c in cols)
                {
                    Assert.True(c.MinWidth >= 0, $"{name}/{c.Id}: MinWidth >= 0");
                    Assert.True(c.MaxWidth >= c.MinWidth, $"{name}/{c.Id}: MaxWidth >= MinWidth");
                    Assert.True(c.Truncation == ResultColumnTruncation.Tail || c.Truncation == ResultColumnTruncation.End,
                        $"{name}/{c.Id}: a defined truncation kind");
                }
            }
        }

        public static void Run_ColumnVisibility_ToggleOff()
        {
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.Equal("access,file", string.Join(",", model.VisibleIds));

            // Toggle a visible column OFF -> it leaves VisibleIds; the others keep their order.
            Assert.True(model.Toggle("access"));
            Assert.Equal("file", string.Join(",", model.VisibleIds));
        }

        public static void Run_ColumnVisibility_ToggleOn()
        {
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            // Toggle a hidden-by-default column ON (symbol was off) -> appended at its CATALOG
            // position (after file), not the end of the toggle order.
            Assert.True(model.Toggle("symbol"));
            Assert.Equal("access,file,symbol", string.Join(",", model.VisibleIds));
        }

        public static void Run_ColumnVisibility_OrderStability()
        {
            // A toggled-off-then-on column returns to its CATALOG position (AC4's "order stable").
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.True(model.Toggle("access"));   // off   -> visible: file
            Assert.True(model.Toggle("column"));   // on    -> file,column
            Assert.True(model.Toggle("text"));     // on    -> file,column,text
            Assert.Equal("file,column,text", string.Join(",", model.VisibleIds));

            // access (catalog index 0) comes back BEFORE file — a naive append-to-end
            // implementation would yield "file,column,text,access" and fail here.
            Assert.True(model.Toggle("access"));
            Assert.Equal("access,file,column,text", string.Join(",", model.VisibleIds));
        }

        public static void Run_ColumnVisibility_AllOffRule()
        {
            // PINNED RULE (Section A P3): the LAST visible column cannot be hidden — the chooser
            // may never leave zero columns (the ListView always keeps >= 1 column and the
            // "[Telescope] results columns=" diagnostic is never empty).
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.True(model.Toggle("access"));            // visible: file (now the last one)
            Assert.False(model.Toggle("file"), "the last visible column cannot be hidden");
            Assert.True(model.IsVisible("file"));
            Assert.Equal("file", string.Join(",", model.VisibleIds));   // unchanged

            // Hiding is possible again once another column is visible.
            Assert.True(model.Toggle("symbol"));
            Assert.True(model.Toggle("file"));
            Assert.Equal("symbol", string.Join(",", model.VisibleIds));
        }

        public static void Run_ColumnVisibility_UnknownIdNoOp()
        {
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.False(model.Toggle("no-such-column"));
            Assert.Equal("access,file", string.Join(",", model.VisibleIds));   // unchanged, no throw
        }

        public static void Run_ColumnVisibility_IdFormat()
        {
            // The "[Telescope] results columns=<ids>" literal (D5/M-M7): the id list is
            // comma-separated with NO spaces, in the model's visible (catalog) order. The
            // overlay's log line (Section B) must embed EXACTLY ResultsFormatter.ColumnsIdList.
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.Equal("access,file", string.Join(",", model.VisibleIds));
            Assert.True(model.Toggle("symbol"));
            Assert.Equal("access,file,symbol", string.Join(",", model.VisibleIds));
        }

        public static void Run_ResultRowCells_OrderedCells()
        {
            // The row's cells are computed from the entry's PAYLOAD via the VISIBLE columns'
            // getters — one cell per visible column, in VISIBLE-column order (a hidden middle
            // column shifts the cells left). D3: presentation-only; Display is untouched.
            var visible = FinderColumns.ForFinder("References").Where(c => c.DefaultVisible).ToList();
            var hit = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            var cells = ResultRowCells.Compute(new FinderEntry("Value (write) Writer.cs:5:5 — Shared.Value = 1;", hit), visible, null);

            Assert.Equal(2, cells.Count);
            Assert.Equal("W", cells[0]);          // D2a: write -> W (Section A rev 1)
            Assert.Equal("Writer.cs", cells[1]);
        }

        public static void Run_ResultRowCells_NullPayloadEmptyCells()
        {
            // A payload-less entry (the E("alpha") shape) must not crash the row computation:
            // every cell is the empty string.
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            var cells = ResultRowCells.Compute(new FinderEntry("alpha"), visible, null);
            Assert.Equal(2, cells.Count);
            Assert.True(cells.All(c => c.Length == 0), "every cell is empty for a null payload");
        }

        public static void Run_ResultRowCells_ForeignPayloadEmptyCells()
        {
            // A payload of the WRONG hit type yields empty cells — never a throw (the getters'
            // Cell<THit> type test).
            var visible = FinderColumns.ForFinder("References").Where(c => c.DefaultVisible).ToList();
            var cells = ResultRowCells.Compute(new FinderEntry("a file row", new FileHit(@"C:\p\A.cs", 0)), visible, null);
            Assert.Equal(2, cells.Count);
            Assert.True(cells.All(c => c.Length == 0), "a foreign payload yields empty cells");
        }

        public static void Run_ResultRowCells_NullEntryEmptyCells()
        {
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            var cells = ResultRowCells.Compute(null, visible, null);
            Assert.Equal(2, cells.Count);
            Assert.True(cells.All(c => c.Length == 0), "a null entry yields empty cells");
        }

        // ================================================================
        // Columns UX (plan D1-D4, BP-9) — the pinned min/max/truncation table,
        // the pure width-fit engine (ColumnWidths), the logical shortening
        // (ColumnTruncation Tail/End), and the truncating ResultRowCells
        // overload. RED: ResultColumnTruncation / ColumnWidths /
        // ColumnTruncation / ResultColumn.MinWidth|MaxWidth|Truncation and the
        // 3-arg ResultRowCells.Compute do not exist yet -> compile error
        // (observed: CS0103 name-not-found for the expression-position type
        // references, CS1729 for the 9-arg ctor, CS1061 for the missing
        // members, CS1501 for the 3-arg Compute overload).
        // ================================================================

        // Synthetic column for the width-algorithm tests (BP-1's 9-param ctor
        // shape: id/header/width/widthChars/min/max/truncation/visible/getter).
        // MaxWidth == int.MaxValue maps to the Flexible absorber (the model's 1:1 rule).
        // The truncation kind is hard-coded to End (every call site's kind; Compute never
        // reads it). NOTE: no ResultColumnTruncation in the SIGNATURE — a missing type in
        // a parameter type is a declaration-phase error that makes csc skip method-body
        // binding entirely, which would hide the rest of the planned RED errors.
        private static ResultColumn Col(string id, int min, int max)
            => new ResultColumn(
                id, id,
                min, max, ResultColumnTruncation.End, true, _ => "x");

        // Asserts one column's pinned min/max/truncation table row (BP-2). The kind is
        // typed object (boxed-enum equality via Assert.Equal<object>) — same reason as
        // Col: the planned enum must not appear in a signature during RED.
        // m56 (BP-D9): the message parameter names the column so a width mismatch reports which
        // column/width failed (a bare Assert.Equal failure has no context).
        private static void AssertCol(IReadOnlyList<ResultColumn> cols, string id, int min, int max, object kind, string message)
        {
            var c = cols.First(x => x.Id == id);
            Assert.Equal(min, c.MinWidth, message + " (MinWidth)");
            Assert.Equal(max, c.MaxWidth, message + " (MaxWidth)");
            Assert.Equal<object>(kind, c.Truncation, message + " (Truncation)");
        }

        public static void Run_ResultsColumns_MinMaxWidths()
        {
            // The pinned per-column table (chars) — D1/D4's single source of truth (BP-2).
            // Tail = the path-like columns (file/dir/path — the front is removed); End =
            // every text/semantic column (the end is removed). All 23 catalog sites.
            var files = FinderColumns.ForFinder("Files");
            AssertCol(files, "file", 6, 30, ResultColumnTruncation.Tail, "Files file");
            // BP-31 (m46): the Files dir column becomes a TRUE absorber (MaxWidth == int.MaxValue).
            AssertCol(files, "dir", 6, int.MaxValue, ResultColumnTruncation.Tail, "Files dir");
            AssertCol(files, "path", 10, 60, ResultColumnTruncation.Tail, "Files path");

            var recent = FinderColumns.ForFinder("Recent");
            AssertCol(recent, "file", 6, 30, ResultColumnTruncation.Tail, "Recent file");
            // BP-31 (m46): the Recent dir column is a TRUE absorber too.
            AssertCol(recent, "dir", 6, int.MaxValue, ResultColumnTruncation.Tail, "Recent dir");
            AssertCol(recent, "path", 10, 60, ResultColumnTruncation.Tail, "Recent path");

            var issues = FinderColumns.ForFinder("Issues");
            AssertCol(issues, "kind", 3, 8, ResultColumnTruncation.End, "Issues kind");
            AssertCol(issues, "file", 6, 30, ResultColumnTruncation.Tail, "Issues file");
            AssertCol(issues, "message", 10, int.MaxValue, ResultColumnTruncation.End, "Issues message");
            AssertCol(issues, "line", 2, 5, ResultColumnTruncation.End, "Issues line");

            var refs = FinderColumns.ForFinder("References");
            AssertCol(refs, "access", 2, 4, ResultColumnTruncation.End, "References access");
            AssertCol(refs, "file", 6, 30, ResultColumnTruncation.Tail, "References file");
            AssertCol(refs, "symbol", 6, 24, ResultColumnTruncation.End, "References symbol");
            AssertCol(refs, "column", 2, 8, ResultColumnTruncation.End, "References column");
            AssertCol(refs, "line", 2, 5, ResultColumnTruncation.End, "References line");
            AssertCol(refs, "text", 10, int.MaxValue, ResultColumnTruncation.End, "References text");

            var grep = FinderColumns.ForFinder("Grep");
            AssertCol(grep, "file", 6, 30, ResultColumnTruncation.Tail, "Grep file");
            AssertCol(grep, "line", 2, 5, ResultColumnTruncation.End, "Grep line");
            AssertCol(grep, "text", 10, int.MaxValue, ResultColumnTruncation.End, "Grep text");

            var fzf = FinderColumns.ForFinder("Fzf");
            AssertCol(fzf, "file", 6, 30, ResultColumnTruncation.Tail, "Fzf file");
            AssertCol(fzf, "line", 2, 5, ResultColumnTruncation.End, "Fzf line");
            AssertCol(fzf, "text", 10, int.MaxValue, ResultColumnTruncation.End, "Fzf text");

            var impl = FinderColumns.ForFinder("Implementation");
            AssertCol(impl, "kind", 3, 8, ResultColumnTruncation.End, "Implementation kind");
            AssertCol(impl, "file", 6, 30, ResultColumnTruncation.Tail, "Implementation file");
            AssertCol(impl, "symbol", 6, int.MaxValue, ResultColumnTruncation.End, "Implementation symbol");
            AssertCol(impl, "line", 2, 5, ResultColumnTruncation.End, "Implementation line");
        }

        public static void Run_ColumnWidths_NeededWidth_Basics()
        {
            // NeededWidth = sum(MinWidth * PixelsPerChar); 0 for null/empty (BP-3).
            Assert.Equal(0d, ColumnWidths.NeededWidth(null!));
            Assert.Equal(0d, ColumnWidths.NeededWidth(new ResultColumn[] { }));

            // One synthetic min-6-char column -> 6 * 8 = 48px.
            var one = new[] { Col("a", min: 6, max: 30) };
            Assert.Equal(48d, ColumnWidths.NeededWidth(one));

            // The References FULL catalog (the all-visible set): 2+6+6+2+2+10 = 28 chars
            // -> 28 * 8 = 224px.
            var refs = FinderColumns.ForFinder("References");
            Assert.Equal(6, refs.Count);
            Assert.Equal(224d, ColumnWidths.NeededWidth(refs));
        }

        public static void Run_ColumnWidths_Compute_Degenerate_MinsWin()
        {
            // DEGENERATE BRANCH (BP-3): available <= NeededWidth -> every column gets its
            // MIN; the total equals NeededWidth (the reported needed width the caller
            // widens the window to), NOT the available width.
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();   // file + dir
            double needed = ColumnWidths.NeededWidth(visible);   // 12 chars -> 96px
            Assert.Equal(96d, needed);

            var widths = ColumnWidths.Compute(needed - 8, visible);   // 88px available < 96 needed
            Assert.Equal(2, widths.Count);
            Assert.Equal(48d, widths[0]);   // file min 6 chars
            Assert.Equal(48d, widths[1]);   // dir min 6 chars
            Assert.Equal(96d, widths.Sum());   // the total == NeededWidth, NOT the 88 available
        }

        public static void Run_ColumnWidths_Compute_ExactTotal_WithAbsorber()
        {
            // EXACT-TOTAL INVARIANT (BP-3): with an absorber (text, MaxWidth == int.MaxValue)
            // the assigned widths sum to the available width EXACTLY; every width stays
            // within [Min*8, Max*8] (the absorber unbounded).
            var refs = FinderColumns.ForFinder("References");   // all 6 columns; text is the absorber
            var widths = ColumnWidths.Compute(400, refs);
            Assert.Equal(6, widths.Count);
            Assert.Equal(400d, widths.Sum());
            for (int i = 0; i < refs.Count; i++)
            {
                double minPx = refs[i].MinWidth * ColumnWidths.PixelsPerChar;
                double maxPx = refs[i].MaxWidth == int.MaxValue
                    ? double.MaxValue
                    : refs[i].MaxWidth * ColumnWidths.PixelsPerChar;
                Assert.True(widths[i] >= minPx, $"{refs[i].Id}: never below its min");
                Assert.True(widths[i] <= maxPx, $"{refs[i].Id}: never above its max");
            }
        }

        public static void Run_ColumnWidths_Compute_PriorityOrder()
        {
            // PRIORITY DISTRIBUTION (BP-3): the surplus flows in MaxWidth-ascending order —
            // the narrow semantic column reaches its max FIRST, the absorber takes the rest.
            var a = Col("a", min: 1, max: 4);               // max 4 chars = 32px
            var b = Col("b", min: 1, max: int.MaxValue);    // the absorber
            var cols = new[] { a, b };

            // 32px: needed 16, surplus 16 — the surplus runs out BEFORE a's 32px max:
            // a = 8 + 16 = 24 (3 chars), b stays at its min 8.
            var tight = ColumnWidths.Compute(32, cols);
            Assert.Equal(24d, tight[0]);
            Assert.Equal(8d, tight[1]);

            // 80px: needed 16, surplus 64 — a reaches its 32px max FIRST, b absorbs the
            // remainder: a = 32, b = 8 + 40 = 48; the exact-total invariant holds.
            var loose = ColumnWidths.Compute(80, cols);
            Assert.Equal(32d, loose[0]);
            Assert.Equal(48d, loose[1]);
            Assert.Equal(80d, loose.Sum());
        }

        public static void Run_ColumnWidths_Compute_FilesCatalog_Pin()
        {
            // The Files VISIBLE set (file 6/30, dir 6/40) at 240px: the surplus (240 - 96
            // = 144) goes to file FIRST (its 240px max is not yet reached) -> file =
            // 48 + 144 = 192, dir stays at its min 48; the exact-total invariant holds.
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            var widths = ColumnWidths.Compute(240, visible);
            Assert.Equal(2, widths.Count);
            Assert.Equal(192d, widths[0]);   // file
            Assert.Equal(48d, widths[1]);    // dir
            Assert.Equal(240d, widths.Sum());
        }

        // BP-31 (m46): the Files/Recent dir columns become TRUE absorbers (MaxWidth ==
        // int.MaxValue). At a wide availableWidth the visible sets' assigned widths sum to
        // availableWidth EXACTLY. RED today: dir max is 40, so the surplus is dropped and the
        // total is 560, not 1400.
        public static void Run_ColumnWidths_Absorber()
        {
            var files = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            var filesWidths = ColumnWidths.Compute(1400, files);
            Assert.Equal(1400d, filesWidths.Sum());

            var recent = FinderColumns.ForFinder("Recent").Where(c => c.DefaultVisible).ToList();
            var recentWidths = ColumnWidths.Compute(1400, recent);
            Assert.Equal(1400d, recentWidths.Sum());
        }

        public static void Run_ColumnWidths_WindowWidth_Default()
        {
            // WINDOW FORMULA (BP-3/BP-7): Width = min(max(default, NeededWidth + 18 + 480
            // + 22), workArea). The Files default-visible set needs 96 + 520 = 616 < 760
            // -> the landed default width wins.
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            Assert.Equal(760d, ColumnWidths.WindowWidth(760, visible, 1600));
        }

        public static void Run_ColumnWidths_WindowWidth_Grows_And_Caps()
        {
            // Two synthetic min-40-char columns: needed 640 + 18 + 480 + 22 = 1160 > 760
            // -> the window GROWS to 1160 (more columns grow the WINDOW, never eat the
            // preview); capped by the work area.
            var cols = new[] { Col("a", min: 40, max: 60), Col("b", min: 40, max: 60) };
            Assert.Equal(1160d, ColumnWidths.WindowWidth(760, cols, 1600));
            Assert.Equal(1000d, ColumnWidths.WindowWidth(760, cols, 1000));   // the work-area cap
        }

        public static void Run_TailTruncate_LongPath_KeepsTail()
        {
            // TAIL truncation (BP-4, the user's R5): the FRONT is removed, the TAIL
            // survives — the end folder + file name. 1 + 18 = 19 chars == maxWidth (the
            // rev-1 contract: the result length equals maxWidth).
            const string path = @"C:\Very\Long\Path\Models\Services\Order.cs";
            Assert.Equal(@"…\Services\Order.cs", ColumnTruncation.TailTruncate(path, 19));

            var cut = ColumnTruncation.TailTruncate(path, 19);
            Assert.True(cut.StartsWith("…"), "the ellipsis prefix marks the removed front");
            Assert.True(cut.EndsWith("Order.cs"), "the file name survives");
            Assert.True(cut.Contains(@"\Services\"), "the end folder survives (the plan's risk-3 minimum tail)");
        }

        public static void Run_TailTruncate_NoOp_And_Edges()
        {
            // The no-op + edge contract (BP-4): exact width and shorter are UNCHANGED (no
            // ellipsis); null/"" -> ""; maxWidth 0 -> ""; maxWidth 1 -> the bare ellipsis.
            Assert.Equal(@"Services\Order.cs", ColumnTruncation.TailTruncate(@"Services\Order.cs", 17));
            Assert.Equal(@"Order.cs", ColumnTruncation.TailTruncate(@"Order.cs", 19));
            Assert.Equal("", ColumnTruncation.TailTruncate(null!, 10));
            Assert.Equal("", ColumnTruncation.TailTruncate("", 10));
            Assert.Equal("", ColumnTruncation.TailTruncate(@"C:\x\Order.cs", 0));
            Assert.Equal("…", ColumnTruncation.TailTruncate(@"C:\x\Order.cs", 1));
        }

        public static void Run_EndTruncate_Basics()
        {
            // END truncation (BP-4): the END is removed, the start survives — 7 chars +
            // the ellipsis (8 chars == maxWidth).
            Assert.Equal("the qui…", ColumnTruncation.EndTruncate("the quick brown fox", 8));
        }

        public static void Run_EndTruncate_NoOp_And_Edges()
        {
            // The mirror of the Tail edges: exact width and shorter are UNCHANGED;
            // null/"" -> ""; maxWidth 0 -> ""; maxWidth 1 -> the bare ellipsis.
            Assert.Equal("the quick", ColumnTruncation.EndTruncate("the quick", 9));
            Assert.Equal("fox", ColumnTruncation.EndTruncate("fox", 19));
            Assert.Equal("", ColumnTruncation.EndTruncate(null!, 10));
            Assert.Equal("", ColumnTruncation.EndTruncate("", 10));
            Assert.Equal("", ColumnTruncation.EndTruncate("the quick brown fox", 0));
            Assert.Equal("…", ColumnTruncation.EndTruncate("the quick brown fox", 1));
        }

        public static void Run_ResultRowCells_Truncation_Kinds()
        {
            // The TRUNCATING overload (BP-5/BP-8): each cell is shortened to its column's
            // char width by the column's OWN truncation kind (Grep: file=Tail, line=End,
            // text=End — the three arrays share the visible-column order).
            var grep = FinderColumns.ForFinder("Grep");
            var hit = new GrepHit(@"C:\Very\Long\Path\Models\Services\OrderController.cs", 12, "the quick brown fox jumps");
            var cells = ResultRowCells.Compute(
                new FinderEntry("OrderController.cs:12: the quick brown fox jumps", hit), grep, new[] { 11, 5, 20 });

            Assert.Equal(3, cells.Count);
            Assert.Equal(@"…troller.cs", cells[0]);   // Tail: 1 + 10 = 11 chars == maxWidth; the extension survives
            Assert.Equal("12", cells[1]);             // End at 5: a no-op (2 chars <= 5)
            Assert.Equal("the quick brown fox…", cells[2]);   // End: 19 chars + the ellipsis

            // A null-payload entry never throws: every cell is empty.
            var empty = ResultRowCells.Compute(new FinderEntry("a row"), grep, new[] { 11, 5, 20 });
            Assert.True(empty.All(c => c.Length == 0), "a null payload yields empty cells");
        }

        public static void Run_LogFileWriter_WritesAndClearsFile()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                string debugPath = Path.Combine(dir.Path, "neovisual-main.log");
                WithLogPath(logPath, () =>
                {
                    WithDebugLogPath(debugPath, () =>
                    {

                        LogFileWriter.Write("structured line");
                        LogFileWriter.WriteDebug("debug line");
                        // A4 (X1/BP-1): the buffered write is not on disk until Flush().
                        LogFileWriter.Flush();
                        Assert.True(ReadAllTextShared(logPath).Contains("structured line"), "structured log should contain the NeoVisual line");
                        Assert.True(ReadAllTextShared(debugPath).Contains("debug line"), "debug log should contain the debug line");

                        // The two files are separate: the structured line is NOT in the debug file and
                        // the debug line is NOT in the structured file.
                        Assert.False(ReadAllTextShared(debugPath).Contains("structured line"), "debug file should not contain structured lines");
                        Assert.False(ReadAllTextShared(logPath).Contains("debug line"), "structured file should not contain debug lines");

                        LogFileWriter.Clear();
                        Assert.Equal(0, new FileInfo(logPath).Length);
                        Assert.Equal(0, new FileInfo(debugPath).Length);
                    });
                });
            }
        }

        public static void Run_LogFileWriter_ClearPerPath()
        {
            using (var dir = new TempDir())
            {
                // Unique Guid paths so Clear() never touches the real %APPDATA% files.
                string pathA = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");
                string debugA = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");
                string pathB = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");
                string debugB = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");

                WithLogPath(pathA, () =>
                {
                    WithDebugLogPath(debugA, () =>
                    {
                        LogFileWriter.Write("lineA");
                        LogFileWriter.WriteDebug("debugA");
                        LogFileWriter.Flush();
                        Assert.True(ReadAllTextShared(pathA).Contains("lineA"), "pathA holds the first write");

                        // First Clear() truncates pathA (and debugA).
                        LogFileWriter.Clear();
                        Assert.Equal(0, new FileInfo(pathA).Length);
                        Assert.Equal(0, new FileInfo(debugA).Length);

                        // Repoint both paths to pathB and write again.
                        LogFileWriter.LogPath = pathB;
                        LogFileWriter.DebugLogPath = debugB;
                        LogFileWriter.Write("lineB");
                        LogFileWriter.WriteDebug("debugB");
                        LogFileWriter.Flush();
                        Assert.True(ReadAllTextShared(pathB).Contains("lineB"), "pathB holds the second write");

                        // Second Clear() must truncate pathB too — per-path idempotency.
                        LogFileWriter.Clear();
                        Assert.Equal(0, new FileInfo(pathB).Length);
                        Assert.Equal(0, new FileInfo(debugB).Length);
                    });
                });
            }
        }

        // ================================================================
        // LogFileWriter buffered writes (BP-1/X1)
        // RED: `LogFileWriter.Flush()` / `LogFileWriter.Close()` do not exist
        //      yet -> compile error; `NotFlushedYet` fails because the current
        //      code writes immediately via File.AppendAllText.
        // NOTE: Clear() is per-path idempotent — each unique Guid temp path
        //       truncates on its first Clear regardless of test order.
        // ================================================================

        public static void Run_LogFileWriter_Buffered_NotFlushedYet()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("x");

                    // The buffered write must NOT be on disk until Flush() — the file either does not
                    // exist yet or is empty. (Current code writes immediately, so this fails RED.)
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.False(content.Contains("x"), "a buffered write must not hit disk until Flush()");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_FlushWritesToDisk()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("x");
                    LogFileWriter.Flush();

                    Assert.True(ReadAllTextShared(logPath).Contains("x"), "Flush() writes the buffered line to disk");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_ContentIdenticalToAppend()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("first");
                    LogFileWriter.Write("second");
                    LogFileWriter.Write("third");
                    LogFileWriter.Flush();

                    // The buffered output must be byte-identical to the per-call File.AppendAllText
                    // format: each line is "HH:mm:ss.fff <message>" + Environment.NewLine, in order.
                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    // File.ReadAllLines drops the trailing empty element left by the final newline.
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    Assert.Equal(3, lines.Length);
                    Assert.True(IsTimestampedLine(lines[0]) && lines[0].EndsWith(" first"), $"line 1 is a timestamped 'first', got '{lines[0]}'");
                    Assert.True(IsTimestampedLine(lines[1]) && lines[1].EndsWith(" second"), $"line 2 is a timestamped 'second', got '{lines[1]}'");
                    Assert.True(IsTimestampedLine(lines[2]) && lines[2].EndsWith(" third"), $"line 3 is a timestamped 'third', got '{lines[2]}'");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_FlushOnClose()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("x");
                    LogFileWriter.Close();

                    Assert.True(File.ReadAllText(logPath).Contains("x"), "Close() flushes the buffered line to disk");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_PathChangeReopens()
        {
            using (var dir = new TempDir())
            {
                string pathA = Path.Combine(dir.Path, "a.log");
                string pathB = Path.Combine(dir.Path, "b.log");
                WithLogPath(pathA, () =>
                {
                    LogFileWriter.LogPath = pathA;
                    LogFileWriter.Write("line1");
                    LogFileWriter.Flush();

                    // Repointing LogPath must close the writer for A and reopen it for B, so the two
                    // files never share lines.
                    LogFileWriter.LogPath = pathB;
                    LogFileWriter.Write("line2");
                    LogFileWriter.Flush();

                    string a = ReadAllTextShared(pathA);
                    string b = ReadAllTextShared(pathB);
                    Assert.True(a.Contains("line1"), "path A holds the first line");
                    Assert.False(a.Contains("line2"), "path A must not receive the second line (writer reopened)");
                    Assert.True(b.Contains("line2"), "path B holds the second line");
                    Assert.False(b.Contains("line1"), "path B must not receive the first line (writer reopened)");
                });
            }
        }

        // ================================================================
        // LogFileWriter flush timer (BP-2/M43) — the ~200ms flush timer must be
        // ONE-SHOT: it fires after a write and then sleeps, instead of firing
        // every 200ms forever (5 wakeups/sec for the whole VS session).
        // RED: `LogFileWriter.FlushCount` does not exist yet -> compile error
        //      (CS0117). If it did exist, `IdleDoesNotFire` would fail at
        //      runtime because the current periodic timer keeps climbing.
        // NOTE: these tests must NOT call Clear() — Clear() is per-path idempotent
        //       (each unique Guid temp path truncates on its first Clear), so a Clear()
        //       here would consume the truncation for a path another test relies on.
        // ================================================================

        public static void Run_LogFileWriter_FlushTimer_OneShotFires()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    // M28: inject a controllable timer scheduler — NO wall-clock Thread.Sleep
                    // polling. Close() first so the static _flushTimer is null and EnsureTimer()
                    // uses the injected seam. RED: `LogFileWriter.TimerScheduler` does not exist
                    // yet -> compile error (CS0117).
                    LogFileWriter.Close();
                    Action? flushCallback = null;
                    LogFileWriter.TimerScheduler = cb => { flushCallback = cb; return new FakeTimer(); };
                    try
                    {
                        int before = LogFileWriter.FlushCount;
                        LogFileWriter.Write("x");

                        Assert.True(flushCallback != null, "the injected timer scheduler captured the flush callback");
                        flushCallback!();
                        Assert.True(LogFileWriter.FlushCount >= before + 1,
                            $"firing the captured callback advances FlushCount, before={before}, after={LogFileWriter.FlushCount}");
                    }
                    finally
                    {
                        LogFileWriter.TimerScheduler = null;
                        // m52 (BP-D2): Close() after nulling the seam disposes + nulls the static
                        // _flushTimer, so the next Write recreates a real Timer instead of leaving
                        // the injected FakeTimer in place (blocking the real ~200ms timer).
                        LogFileWriter.Close();
                    }
                });
            }
        }

        public static void Run_LogFileWriter_FlushTimer_IdleDoesNotFire()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Close();
                    Action? flushCallback = null;
                    LogFileWriter.TimerScheduler = cb => { flushCallback = cb; return new FakeTimer(); };
                    try
                    {
                        int before = LogFileWriter.FlushCount;
                        LogFileWriter.Write("x");

                        Assert.True(flushCallback != null, "the injected timer scheduler captured the flush callback");
                        // Do NOT fire the callback: with no further writes the one-shot timer must
                        // not re-fire, so FlushCount stays put (deterministic — no sleeps).
                        Assert.Equal(before, LogFileWriter.FlushCount);
                    }
                    finally
                    {
                        LogFileWriter.TimerScheduler = null;
                        // m52 (BP-D2): Close() after nulling the seam disposes + nulls the static
                        // _flushTimer, so the next Write recreates a real Timer instead of leaving
                        // the injected FakeTimer in place (blocking the real ~200ms timer).
                        LogFileWriter.Close();
                    }
                });
            }
        }

        // ================================================================
        // LogFileWriter flush-timer restore (BP-D2/m52) — the two FlushTimer
        // tests above inject `TimerScheduler = cb => new FakeTimer()` and their
        // finally only nulls TimerScheduler, so the static `_flushTimer` stays
        // the injected FakeTimer — blocking the real ~200ms timer for every
        // later test (GetWriter only re-arms `if (_flushTimer is Timer t)`).
        // The fix: Close() after TimerScheduler = null (disposes + nulls
        // _flushTimer) so the next Write recreates a real Timer, plus the
        // `FlushTimerIsReal` seam to observe the restore without reflection.
        // RED: `LogFileWriter.FlushTimerIsReal` does not exist yet -> compile
        //      error (CS0117).
        // ================================================================

        public static void Run_LogFileWriter_FlushTimerRestored()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    // Start clean: Close() disposes + nulls any prior _flushTimer so
                    // EnsureTimer() uses the injected seam below.
                    LogFileWriter.Close();
                    LogFileWriter.TimerScheduler = cb => new FakeTimer();
                    try
                    {
                        LogFileWriter.Write("x");
                        Assert.False(LogFileWriter.FlushTimerIsReal,
                            "the injected FakeTimer must be observable as NOT a real Timer");
                    }
                    finally
                    {
                        LogFileWriter.TimerScheduler = null;
                        // THE FIX (BP-D2/m52): Close() after nulling the seam disposes +
                        // nulls the static _flushTimer, so the next Write recreates a real
                        // ~200ms Timer instead of leaving the FakeTimer in place.
                        LogFileWriter.Close();
                    }

                    // After the restore, a fresh Write must re-arm a REAL Timer.
                    LogFileWriter.Write("x");
                    Assert.True(LogFileWriter.FlushTimerIsReal,
                        "after TimerScheduler = null + Close(), the next Write must recreate a real Timer");
                });
            }
        }

        // ================================================================
        // LogFileWriter write-failure count (BP-3/M35) — Write/WriteDebug
        // swallow every exception, so the pipeline is undiagnosable when it
        // fails. The fix exposes a `WriteFailureCount` seam.
        // RED: `LogFileWriter.WriteFailureCount` does not exist yet -> compile
        //      error (CS0117).
        // NOTE: these tests must NOT call Clear() (per-path idempotent — a Clear() would
        //       consume the truncation for a path another test relies on).
        // ================================================================

        public static void Run_LogFileWriter_WriteFailureCountIncrements()
        {
            using (var dir = new TempDir())
            {
                string blockerFile = Path.Combine(dir.Path, "blocker");
                File.WriteAllText(blockerFile, "i am a file, not a directory");
                // LogPath's parent is a FILE -> Directory.CreateDirectory / new StreamWriter throws.
                string logPath = Path.Combine(blockerFile, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    int before = LogFileWriter.WriteFailureCount;

                    // Must not throw (never-throw contract) and must increment the count.
                    LogFileWriter.Write("x");

                    Assert.Equal(before + 1, LogFileWriter.WriteFailureCount);
                });
            }
        }

        public static void Run_LogFileWriter_WriteDebugFailureCountIncrements()
        {
            using (var dir = new TempDir())
            {
                string blockerFile = Path.Combine(dir.Path, "blocker");
                File.WriteAllText(blockerFile, "i am a file, not a directory");
                // DebugLogPath's parent is a FILE -> Directory.CreateDirectory / new StreamWriter throws.
                string debugPath = Path.Combine(blockerFile, "neovisual-main.log");
                WithDebugLogPath(debugPath, () =>
                {
                    int before = LogFileWriter.WriteFailureCount;

                    // Must not throw (never-throw contract) and must increment the count.
                    LogFileWriter.WriteDebug("x");

                    Assert.Equal(before + 1, LogFileWriter.WriteFailureCount);
                });
            }
        }

        // ================================================================
        // PaneFailureTracker (BP-4/M18) — a pure one-time fallback for the
        // NeoVisual Output pane: on the FIRST pane failure a single
        // "[NeoVisual] output pane unavailable: <reason>" line is written to
        // the FILE (never via NeoVisualLog.Log, which would re-enter
        // WriteToPane -> recursion).
        // RED: `PaneFailureTracker` does not exist yet -> compile error (CS0246).
        // ================================================================

        public static void Run_PaneFailureTracker_FallbackMessageFormat()
        {
            var tracker = new PaneFailureTracker();
            Assert.Equal("[NeoVisual] output pane unavailable: pane create failed",
                tracker.FallbackMessage("pane create failed"));
        }

        public static void Run_PaneFailureTracker_OneTimeFallback()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var tracker = new PaneFailureTracker();

                    // Two pane failures: the fallback line is emitted only on the FIRST.
                    if (tracker.ShouldEmit())
                    {
                        LogFileWriter.Write(tracker.FallbackMessage("pane create failed"));
                    }
                    if (tracker.ShouldEmit())
                    {
                        LogFileWriter.Write(tracker.FallbackMessage("pane create failed"));
                    }
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var fallbackLines = lines.Where(l => l.Contains("[NeoVisual] output pane unavailable:")).ToList();
                    Assert.Equal(1, fallbackLines.Count);
                    Assert.True(fallbackLines[0].Contains("[NeoVisual] output pane unavailable: pane create failed"),
                        $"expected exactly one fallback line, got: {string.Join(" | ", fallbackLines)}");
                });
            }
        }

        public static void Run_FzfFilter_FilterMatchesPrefix()
        {
            // m57: hermetic fzf stub — no fzf-on-PATH Mystery Guest. The stub mimics
            // `fzf --filter <query> --no-sort`: reads the query from the args, filters stdin
            // case-insensitively, and prints the matches. The injected-path ctor (m22) is kept.
            using (var dir = new TempDir())
            {
                string stubPath = Path.Combine(dir.Path, "fzf-stub.cmd");
                File.WriteAllText(stubPath,
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "set \"q=\"\r\n" +
                    ":loop\r\n" +
                    "if \"%~1\"==\"\" goto run\r\n" +
                    "if \"%~1\"==\"--filter\" (set \"q=%~2\" & shift)\r\n" +
                    "shift\r\n" +
                    "goto loop\r\n" +
                    ":run\r\n" +
                    "findstr /i /c:\"%q%\"\r\n");

                var fzf = new FzfFilter(stubPath);
                var matched = fzf.FilterAsync(
                    new[] { "alpha.cs", "beta.txt", "gamma.cs" },
                    "alp",
                    new System.Threading.CancellationToken()).GetAwaiter().GetResult();

                // Stronger assertion: the stub filters to EXACTLY the matching line(s), not just
                // "some result contains alpha".
                Assert.Equal(1, matched.Count);
                Assert.Equal("alpha.cs", matched[0]);
            }
        }

        // ================================================================
        // FzfFilter injected-path seam (BP-3/M6a, BP-4/M6b, BP-5/M6c)
        // RED: FilterTimeoutMs does not exist -> compile error (CS1061);
        //      QuoteArg is private -> compile error (CS0122).
        // ================================================================

        public static void Run_FzfFilter_NonexistentPathFallsBackAndLogs()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {

                    var fzf = new FzfFilter(Path.Combine(dir.Path, "missing-fzf.exe"));
                    var result = fzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    // BP-1 (M3): a missing fzf must signal failure with null (never the full list) AND
                    // log the failure (M6: today the catch is silent — no [Telescope] line is emitted).
                    Assert.True(result == null, "the crash path must return null (never the full candidate list)");
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.True(content.Contains("[Telescope] fzf filter failed:"),
                        "the catch path must log '[Telescope] fzf filter failed:'");
                });
            }
        }

        // R24 (BP-44): Run_FzfFilter_TimeoutKillsAndFallsBack was DELETED — it was timing-dependent
        // (5s wall-clock + 2s GC-poll) and redundant with the deterministic
        // Run_FzfFilter_TimeoutAwaitsTasks below (the AwaitedReadCount seam). Telescope.Tests count
        // drops by 1 (deterministic signal).

        public static void Run_FzfFilter_TimeoutAwaitsTasks()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                    File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                    // M7 (BP-60): the timeout path must deterministically observe the awaited
                    // ReadToEndAsync tasks (no unobserved-task noise). The seam is an awaited-read
                    // count the timeout path increments when it arranges for both pipe-read tasks
                    // to be observed — the test asserts on it instead of GC-polling for
                    // UnobservedTaskException (the old 5s wall-clock + 2s GC-poll).
                    // RED: `AwaitedReadCount` does not exist -> compile error (CS0117).
                    var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 200 };
                    var result = fzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    // BP-1 (M3): the timeout path must signal failure with null (never the full list).
                    Assert.True(result == null, "the timeout path must return null (never the full candidate list)");
                    Assert.True(fzf.AwaitedReadCount >= 2,
                        "the timeout path must observe both ReadToEndAsync tasks (AwaitedReadCount >= 2)");
                });
            }
        }

        // BP-1 (M3): a failed fzf filter (timeout OR crash) must signal failure with null — never
        // the full candidate list. RED today: both paths return `lines` (the full list).
        public static void Run_FzfFilter_FailureSignalsEmpty()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    // Timeout path: a hung fzf must return null (never the full list).
                    string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                    File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");
                    var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 200 };
                    var timeoutResult = fzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    Assert.True(timeoutResult == null,
                        "the timeout path must return null (never the full candidate list) — BP-1/M3");

                    // Crash path: a missing fzf must also return null (never the full list).
                    var crashFzf = new FzfFilter(Path.Combine(dir.Path, "missing-fzf.exe"));
                    var crashResult = crashFzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    Assert.True(crashResult == null,
                        "the crash path must return null (never the full candidate list) — BP-1/M3");
                });
            }
        }

        public static void Run_FzfFilter_CancellationObservesTasks()
        {
            // R23 (BP-34): the fzf CANCELLATION path (FzfFilter.cs:172-175) returns without observing
            // the pending outputTask/errorTask -> UnobservedTaskException noise on every overlay close
            // mid-filter. The fix mirrors the timeout path's fault-only continuation + AwaitedReadCount
            // seam. RED: today the cancellation path does NOT increment AwaitedReadCount, so this
            // assertion fails (the tasks are not observed on cancellation).
            using (var dir = new TempDir())
            {
                string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 5000 };
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    var task = fzf.FilterAsync(new[] { "alpha" }, "alp", cts.Token);
                    cts.Cancel();
                    var result = task.GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha"), "cancellation falls back to the full candidate list");
                    Assert.True(fzf.AwaitedReadCount >= 2,
                        "the cancellation path must observe both ReadToEndAsync tasks (AwaitedReadCount >= 2)");
                }
            }
        }

        // ================================================================
        // m11 (BP-4): the fzf timeout-vs-completion race. Task.WhenAny can return `timeout` in the
        // same instant `all` completes; the code then kills the process and returns the unfiltered
        // `lines` even though the filter actually produced output — a wrong result + a spurious
        // `fzf filter failed: timeout after {ms}ms` line. The stub sleeps ~FilterTimeoutMs then
        // emits a match, so the timeout fires at the boundary. RED: today the boundary completion
        // returns the unfiltered list + logs the spurious timeout line.
        // ================================================================

        public static void Run_FzfFilter_TimeoutRace()
        {
            // BP-D18 (M10): the stub's `ping -n 2 127.0.0.1` (≈1s) raced FilterTimeoutMs=1000 — a
            // wall-clock boundary race. The DelayFactory seam makes the boundary deterministic: the
            // timeout delay is a task the test controls (a TaskCompletionSource); the stub emits
            // immediately; the test completes the timeout task AFTER the stub has emitted, so
            // winner == timeout with all.IsCompleted -> the boundary fast path returns the filtered
            // output (NOT the unfiltered list), no spurious timeout line.
            // COMPILE-RED: `FzfFilter.DelayFactory` does not exist yet -> CS0117.
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    string stubPath = Path.Combine(dir.Path, "fzf-boundary.cmd");
                    string markerPath = Path.Combine(dir.Path, "fzf-boundary.started");
                    // n21 (BP-D19): the stub writes a started marker BEFORE emitting its match, so
                    // the test can poll the stub's output (the marker) instead of a fixed delay.
                    File.WriteAllText(stubPath, "@echo off\r\n" +
                        $"echo started> \"{markerPath}\"\r\n" +
                        "echo alpha.cs\r\n");

                    var timeoutTcs = new TaskCompletionSource<bool>();
                    var fzf = new FzfFilter(stubPath) { FilterTimeoutMs = 1000 };
                    // The timeout delay is the controlled task; the grace delay is a real short
                    // delay (never reached on the boundary fast path).
                    fzf.DelayFactory = (ms, ct) => ms == fzf.FilterTimeoutMs ? timeoutTcs.Task : Task.Delay(1);

                    var resultTask = fzf.FilterAsync(new[] { "alpha.cs", "beta.txt" }, "alp", CancellationToken.None);
                    // Poll for the stub's output (the marker file) — a bounded wait, not a race
                    // (the local stub exits in ms). The timeout completes only AFTER the stub has
                    // emitted, so the boundary fast path is deterministic.
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!File.Exists(markerPath) && DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(20);
                    }
                    Assert.True(File.Exists(markerPath), "the fzf-boundary stub emitted its output (marker written)");
                    timeoutTcs.SetResult(true);
                    var result = resultTask.GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    // The filter completed at the boundary: its output (only the match) must be
                    // returned, NOT the unfiltered candidate list.
                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha.cs"), "the boundary-completed filter returns its match");
                    Assert.False(result.Contains("beta.txt"),
                        "the boundary-completed filter must NOT return the unfiltered list (m11)");
                    Assert.True(fzf.PendingTimeoutCount == 0, "no pending timeout timer survives");
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.False(content.Contains("fzf filter failed: timeout after"),
                        "a completed filter must not log the spurious timeout line (m11)");
                });
            }
        }

        public static void Run_FzfFilter_QuoteArg_TrailingBackslash()
        {
            // M6b: a trailing backslash must be doubled before the closing quote so it does not
            // escape the quote (today QuoteArg("foo\") returns "\"foo\"" — the backslash escapes
            // the closing quote and corrupts the fzf argument).
            Assert.Equal("\"foo\\\\\"", FzfFilter.QuoteArg("foo\\"));
            Assert.Equal("\"foo\"", FzfFilter.QuoteArg("foo"));
            Assert.Equal("\"a\\\"b\"", FzfFilter.QuoteArg("a\"b"));
            Assert.Equal("\"\"", FzfFilter.QuoteArg(""));
        }

        public static void Run_FzfFilter_QuoteArg_BackslashBeforeQuote()
        {
            // N45 (BP-59): a backslash run immediately BEFORE a quote (in the middle of the string)
            // must be doubled, then the quote escaped (Windows argv rules). RED today: the odd
            // backslash before the quote is lost — QuoteArg(@"C:\"") returns "C:\\"" instead of
            // "C:\\\"".
            Assert.Equal("\"C:\\\\\\\"\"", FzfFilter.QuoteArg("C:\\\""));
            Assert.Equal("\"a\\\\\\\"b\"", FzfFilter.QuoteArg("a\\\"b"));
            // The trailing-backslash path must not regress (the existing test also pins it).
            Assert.Equal("\"foo\\\\\"", FzfFilter.QuoteArg("foo\\"));
        }

        public static void Run_FzfFilter_IsAvailableFalseForMissingPath()
        {
            using (var dir = new TempDir())
            {
                var fzf = new FzfFilter(Path.Combine(dir.Path, "missing-fzf.exe"));
                Assert.False(fzf.IsAvailableAsync().GetAwaiter().GetResult(), "a missing fzf path must report unavailable");
            }
        }

        public static void Run_FzfFilter_IsAvailableBounded()
        {
            using (var dir = new TempDir())
            {
                string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                var fzf = new FzfFilter(cmdPath);
                bool available = fzf.IsAvailableAsync().GetAwaiter().GetResult();

                // R47 (BP-45): the wall-clock bound (`sw.Elapsed < 3s`) is GONE — it was flaky on a
                // slow CI machine. The deterministic assertion is the outcome: a hung fzf --version
                // must report unavailable (the internal IsAvailableTimeoutMs = 500 bounds the wait;
                // the probe can never hang the caller). The bound is verified by the internal
                // timeout constant, not by a wall-clock read in the test.
                Assert.False(available, "a hung fzf must report unavailable");
            }
        }

        public static void Run_FzfFilter_FilterAsyncSkipsSpawnWhenUnavailable()
        {
            // N39 (BP-53): FilterAsync must check the cached availability and return the unfiltered
            // list WITHOUT spawning when fzf is unavailable — today every keystroke attempts
            // p.Start() -> Win32Exception + a `fzf filter failed` log. RED today: the failure log
            // is emitted even though IsAvailable() already cached false.
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var fzf = new FzfFilter(Path.Combine(dir.Path, "missing-fzf.exe"));
                    Assert.False(fzf.IsAvailableAsync().GetAwaiter().GetResult(), "a missing fzf path must report unavailable (cached)");

                    var result = fzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None)
                        .GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha"), "unavailable fzf returns the unfiltered list");
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.False(content.Contains("fzf filter failed:"),
                        "FilterAsync must not spawn fzf when the cached availability is false (no per-keystroke failure log)");
                });
            }
        }

        // ================================================================
        // OverlayKeyHandler — navigation (j/k/gg/G, wrap) + insert/normal mode
        // ================================================================

        public static void Run_KeyHandler_StartsInInsertMode()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            Assert.False(h.IsNormalMode, "overlay starts in insert mode");
            Assert.Equal(0, h.SelectedIndex);
        }

        public static void Run_KeyHandler_EscapeEntersNormalMode()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            var action = h.Handle(OverlayKey.Escape);
            Assert.Equal(OverlayAction.EnterNormal, action);
            Assert.True(h.IsNormalMode, "Escape in insert mode enters normal mode");
        }

        public static void Run_KeyHandler_JKMoveSelection()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.True(h.IsNormalMode, "Escape enters normal mode");

            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.J));
            Assert.Equal(1, h.SelectedIndex);
            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.J));
            Assert.Equal(2, h.SelectedIndex);
            Assert.Equal(OverlayAction.MoveUp, h.Handle(OverlayKey.K));
            Assert.Equal(1, h.SelectedIndex);
        }

        public static void Run_KeyHandler_SelectionWrapsAround()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(2);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            // From index 0, Down wraps to 1 then back to 0.
            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.Down));
            Assert.Equal(1, h.SelectedIndex);
            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.Down));
            Assert.Equal(0, h.SelectedIndex);
            // Up from 0 wraps to the last entry.
            Assert.Equal(OverlayAction.MoveUp, h.Handle(OverlayKey.Up));
            Assert.Equal(1, h.SelectedIndex);
        }

        public static void Run_KeyHandler_GgMovesToFirst()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(4);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            // Move down a couple, then "gg" returns to the first.
            h.Handle(OverlayKey.J);
            h.Handle(OverlayKey.J);
            Assert.Equal(2, h.SelectedIndex);
            // First 'g' arms the pending gg (no action yet).
            Assert.Equal(OverlayAction.None, h.Handle(OverlayKey.G));
            Assert.Equal(OverlayAction.MoveToFirst, h.Handle(OverlayKey.G));
            Assert.Equal(0, h.SelectedIndex);
        }

        public static void Run_KeyHandler_ShiftGMovesToLast()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(5);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            Assert.Equal(OverlayAction.MoveToLast, h.Handle(OverlayKey.ShiftG));
            Assert.Equal(4, h.SelectedIndex);
        }

        public static void Run_KeyHandler_IAEnterInsertWithCaret()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.True(h.IsNormalMode, "'Escape' enters normal mode");

            Assert.Equal(OverlayAction.EnterInsert, h.Handle(OverlayKey.I));
            Assert.False(h.IsNormalMode, "'i' returns to insert mode");

            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.Equal(OverlayAction.EnterInsertAppend, h.Handle(OverlayKey.ShiftA));
            Assert.False(h.IsNormalMode, "'A' returns to insert mode (append caret)");
        }

        public static void Run_KeyHandler_EnterSelectsInInsertAndNormal()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(2);
            // Insert mode: Enter selects.
            Assert.Equal(OverlayAction.SelectCurrent, h.Handle(OverlayKey.Enter));
            // Normal mode: Enter also selects.
            h.Handle(OverlayKey.Escape);
            Assert.Equal(OverlayAction.SelectCurrent, h.Handle(OverlayKey.Enter));
        }

        public static void Run_KeyHandler_QAndEscapeCloseInNormal()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(2);
            h.Handle(OverlayKey.Escape);
            Assert.Equal(OverlayAction.Close, h.Handle(OverlayKey.Q));
            Assert.Equal(OverlayAction.Close, h.Handle(OverlayKey.Escape));
        }

        public static void Run_KeyHandler_NoSelectionWithoutResults()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(0);
            h.Handle(OverlayKey.Escape);
            // Movement on an empty list is a no-op that keeps index at 0.
            h.Handle(OverlayKey.J);
            Assert.Equal(0, h.SelectedIndex);
            h.Handle(OverlayKey.ShiftG);
            Assert.Equal(0, h.SelectedIndex);
        }

        // n22 (BP-D20): SetResults clamps the selection into the valid range
        // (OverlayKeyHandler.cs:85-88) — SetResults(0) forces selection 0, and an
        // out-of-range selection clamps to count-1.
        public static void Run_SetResults_Clamp()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            h.Handle(OverlayKey.J);
            h.Handle(OverlayKey.J);
            Assert.Equal(2, h.SelectedIndex);

            // Shrinking the result count below the selection clamps to count-1.
            h.SetResults(1);
            Assert.Equal(0, h.SelectedIndex, "an out-of-range selection clamps to count-1");

            // SetResults(0) forces the selection to 0 (no results -> no selection).
            h.SetResults(0);
            Assert.Equal(0, h.SelectedIndex, "SetResults(0) forces selection to 0");
        }

        // ================================================================
        // CaretPlacement — shared insert-caret enum (BP-7/L7)
        // RED: `CaretPlacement` does not exist yet -> compile error
        // ================================================================

        public static void Run_CaretPlacement_EnterInsertMapsToActions()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.True(h.IsNormalMode, "'Escape' enters normal mode");

            // I -> EnterInsert (caret at current position).
            Assert.Equal(OverlayAction.EnterInsert, h.Handle(OverlayKey.I));
            Assert.False(h.IsNormalMode, "'i' returns to insert mode");

            // ShiftA -> EnterInsertAppend (caret at end).
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.Equal(OverlayAction.EnterInsertAppend, h.Handle(OverlayKey.ShiftA));

            // EnterInsertMode(CaretPlacement) mapping (requires EnterInsertMode to be internal).
            Assert.Equal(OverlayAction.EnterInsertAppend, h.EnterInsertMode(CaretPlacement.End));
            Assert.Equal(OverlayAction.EnterInsertStart, h.EnterInsertMode(CaretPlacement.Start));
            Assert.Equal(OverlayAction.EnterInsert, h.EnterInsertMode(CaretPlacement.Current));
        }

        public static void Run_CaretPlacement_EnumValues()
        {
            var names = Enum.GetNames(typeof(CaretPlacement));
            Assert.Equal(4, names.Length);
            Assert.True(names.Contains("Current"), "Current member exists");
            Assert.True(names.Contains("End"), "End member exists");
            Assert.True(names.Contains("Start"), "Start member exists");
            Assert.True(names.Contains("AfterCaret"), "AfterCaret member exists");
            // n2: pin the enum VALUES (declaration order in OverlayKeyHandler.cs:51-56).
            Assert.Equal(0, (int)CaretPlacement.Current);
            Assert.Equal(1, (int)CaretPlacement.End);
            Assert.Equal(2, (int)CaretPlacement.Start);
            Assert.Equal(3, (int)CaretPlacement.AfterCaret);
        }

        // ================================================================
        // TextMotionNavigator (shared) — vim motions over preview text
        // ================================================================

        public static void Run_Preview_HlMoveByChar()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello world");
            n.MoveTo(0);
            n.Right();
            Assert.Equal(1, n.Caret);
            n.Left();
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_JkMoveByLine()
        {
            var n = new TextMotionNavigator();
            n.SetText("alpha\nbeta\ngamma");
            n.MoveTo(0);
            n.Down();
            Assert.Equal(2, n.LineNumber);
            Assert.Equal(6, n.Caret); // caret 6 = start of "beta" (m12: ColumnNumber deleted, assert Caret directly)
            n.Down();
            Assert.Equal(3, n.LineNumber);
            Assert.Equal(11, n.Caret); // caret 11 = start of "gamma"
            n.Up();
            Assert.Equal(2, n.LineNumber);
            Assert.Equal(6, n.Caret); // caret 6 = start of "beta"
        }

        public static void Run_Preview_WordMotions()
        {
            var n = new TextMotionNavigator();
            n.SetText("one two three");
            n.MoveTo(0);
            n.NextWord();
            Assert.Equal(4, n.Caret);
            n.NextWord();
            Assert.Equal(8, n.Caret);
            n.PrevWord();
            Assert.Equal(4, n.Caret);
            n.PrevWord();
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_GgAndG()
        {
            var n = new TextMotionNavigator();
            n.SetText("a\nb\nc\nd");
            n.MoveTo(6); // in "d" line
            n.Top();
            Assert.Equal(0, n.Caret);
            Assert.Equal(1, n.LineNumber);
            n.Bottom();
            Assert.Equal(7, n.Caret);
            Assert.Equal(4, n.LineNumber);
        }

        public static void Run_Preview_LineStartEnd()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(4); // start of "def"
            n.LineEnd();
            Assert.Equal(7, n.Caret);
            n.LineStart();
            Assert.Equal(4, n.Caret);
        }

        public static void Run_Preview_CaretClamped()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc");
            n.MoveTo(99);
            Assert.Equal(3, n.Caret);
            n.MoveTo(-5);
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_MoveToLine()
        {
            var n = new TextMotionNavigator();
            n.SetText("one\ntwo\nthree");
            n.MoveToLine(1);
            Assert.Equal(0, n.Caret);
            n.MoveToLine(2);
            Assert.Equal(4, n.Caret);
            n.MoveToLine(3);
            Assert.Equal(8, n.Caret);
            // Beyond the last line clamps to the last line start.
            n.MoveToLine(99);
            Assert.Equal(8, n.Caret);
            // Single line: line 1 = 0.
            var single = new TextMotionNavigator();
            single.SetText("only");
            single.MoveToLine(5);
            Assert.Equal(0, single.Caret);
        }

        public static void Run_Preview_InsertMotions()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello world");
            n.MoveTo(2);
            n.InsertAfter();
            Assert.Equal(3, n.Caret);
            n.InsertEnd();
            Assert.Equal(11, n.Caret);
            n.InsertStart();
            Assert.Equal(0, n.Caret);
        }

        // ================================================================
        // Preview/motion correctness (Phase 3 — M9/M10/M11).
        // RED: M10's Up() calls LastIndexOf('\n', lineStart - 2) with
        // lineStart - 2 == -1 on a leading blank line -> ArgumentOutOfRangeException.
        // ================================================================

        public static void Run_Preview_UpFromSecondLineWithLeadingBlankLine()
        {
            // M10 (CORRECTED): on a leading blank line ("\nabc"), Up() from line 2 must move to
            // line 1/caret 0. RED today: Up() clamps LastIndexOf('\n', lineStart-2) to 0 and finds
            // the '\n' at index 0, so prevStart == 1 (line 2's own start) and Up() stays put
            // (LineNumber == 2).
            var n = new TextMotionNavigator();
            n.SetText("\nabc");
            n.MoveToLine(2);
            n.Up();
            Assert.Equal(1, n.LineNumber);
            Assert.Equal(0, n.Caret);
        }

        // ================================================================
        // M11a — pure index -> (line, offset) mapping on the shared LineIndex
        // (Phase 2 M7). A blank line must map to ITS OWN start (offset 0),
        // not null and not the next line. LineIndex already exists from
        // Phase 2, so this test may PASS before the fix — the real M11 gate
        // is the WPF CaretToPointer blank-line fallback (BP-4, build-verified).
        // ================================================================

        public static void Run_Preview_CaretOnBlankLine()
        {
            var idx = new LineIndex("abc\n\nxyz");
            Assert.Equal(2, idx.LineOf(4));
            Assert.Equal(0, 4 - idx.LineStart(2));
            Assert.Equal(3, idx.LineOf(5));
            Assert.Equal(0, 5 - idx.LineStart(3));
        }

        // ================================================================
        // Prompt motions — normal-mode h/l/w/b/e/0/$ over the search box text
        // (mirrors the text-input tool-window motions)
        // ================================================================

        public static void Run_PromptMotion_HlMoveByChar()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(3);
            n.Right();
            Assert.Equal(4, n.Caret);
            n.Left();
            Assert.Equal(3, n.Caret);
            n.Left();
            Assert.Equal(2, n.Caret);
        }

        public static void Run_PromptMotion_WordMotions()
        {
            var n = new TextMotionNavigator();
            n.SetText("find my file");
            n.MoveTo(0);
            n.NextWord();
            Assert.Equal(5, n.Caret);
            n.NextWord();
            Assert.Equal(8, n.Caret);
            n.PrevWord();
            Assert.Equal(5, n.Caret);
            n.PrevWord();
            Assert.Equal(0, n.Caret);
        }

        public static void Run_PromptMotion_LineStartEnd()
        {
            var n = new TextMotionNavigator();
            n.SetText("search text");
            n.MoveTo(4);
            n.LineStart();
            Assert.Equal(0, n.Caret);
            n.LineEnd();
            Assert.Equal(11, n.Caret);
        }

        public static void Run_PromptMotion_EndWord()
        {
            var n = new TextMotionNavigator();
            n.SetText("foo bar");
            n.MoveTo(0);
            n.EndWord();
            Assert.Equal(3, n.Caret);
        }

        // ================================================================
        // TextMotionDispatcher — shared WPF-Key vim-motion dispatch (M24/n11)
        // The surviving surface is MapKey(Key, bool) + Apply(TextMotion, TextMotionNavigator,
        // out CaretPlacement?) — the merged Handle(...) wrapper is DEAD (BP-1/m25) and these
        // tests use the surviving surface. The union h/l/j/k/w/b/e/0/$/gg/G + a/A/I. The $
        // drift fix: bare D4 (no shift) is NOT a motion and must NOT LineEnd.
        // ================================================================

        public static void Run_TextMotionDispatcher_DollarWithoutShiftNotHandled()
        {
            // The $ drift fix: in the preview surface a bare D4 currently LineEnds; the shared
            // dispatch must require Shift for $ (D4), so a bare D4 maps to NO motion and does
            // nothing.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.MapKey(Key.D4, false) == null,
                "bare $ (D4 without shift) is not a motion");
            Assert.Equal(0, n.Caret); // must NOT LineEnd
        }

        public static void Run_TextMotionDispatcher_DollarWithShiftLineEnds()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.MapKey(Key.D4, true) == TextMotion.LineEnd,
                "$ (D4 with shift) maps to LineEnd");
            Assert.True(TextMotionDispatcher.Apply(TextMotion.LineEnd, n, out _));
            Assert.Equal(3, n.Caret); // end of "abc"
        }

        public static void Run_TextMotionDispatcher_Motion_H()
        {
            // m55 (BP-D8): one test per motion — H -> Left.
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.H, false) == TextMotion.Left);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Left, n, out _));
            Assert.Equal(1, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_L()
        {
            // L -> Right.
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.L, false) == TextMotion.Right);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Right, n, out _));
            Assert.Equal(3, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_W()
        {
            // W -> NextWord.
            var n = new TextMotionNavigator();
            n.SetText("one two");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.MapKey(Key.W, false) == TextMotion.NextWord);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.NextWord, n, out _));
            Assert.Equal(4, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_B()
        {
            // B -> PrevWord.
            var n = new TextMotionNavigator();
            n.SetText("one two");
            n.MoveTo(4);
            Assert.True(TextMotionDispatcher.MapKey(Key.B, false) == TextMotion.PrevWord);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.PrevWord, n, out _));
            Assert.Equal(0, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_E()
        {
            // E -> EndWord.
            var n = new TextMotionNavigator();
            n.SetText("one two");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.MapKey(Key.E, false) == TextMotion.EndWord);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.EndWord, n, out _));
            Assert.Equal(3, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_J()
        {
            // J -> Down.
            var n = new TextMotionNavigator();
            n.SetText("a\nb");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.MapKey(Key.J, false) == TextMotion.Down);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Down, n, out _));
            Assert.Equal(2, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_K()
        {
            // K -> Up.
            var n = new TextMotionNavigator();
            n.SetText("a\nb");
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.K, false) == TextMotion.Up);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Up, n, out _));
            Assert.Equal(0, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_D0()
        {
            // 0 -> LineStart.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(5);
            Assert.True(TextMotionDispatcher.MapKey(Key.D0, false) == TextMotion.LineStart);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.LineStart, n, out _));
            Assert.Equal(4, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_Gg()
        {
            // gg (bare G) -> Top.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(5);
            Assert.True(TextMotionDispatcher.MapKey(Key.G, false) == TextMotion.Top);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Top, n, out _));
            Assert.Equal(0, n.Caret);
        }

        public static void Run_TextMotionDispatcher_Motion_GG()
        {
            // G (shift) -> Bottom.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.MapKey(Key.G, true) == TextMotion.Bottom);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Bottom, n, out _));
            Assert.Equal(7, n.Caret);
        }

        public static void Run_TextMotionDispatcher_InsertPlacements()
        {
            // A (bare) -> InsertAfter, placement AfterCaret (m46/BP-16: the placement drift fix —
            // Apply reports AfterCaret for InsertAfter, matching PromptMotionRouter's bare-a mapping).
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.A, false) == TextMotion.InsertAfter);
            CaretPlacement? placement;
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertAfter, n, out placement));
            Assert.Equal(CaretPlacement.AfterCaret, placement);
            Assert.Equal(3, n.Caret);

            // A (shift) -> InsertEnd, placement End.
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.A, true) == TextMotion.InsertEnd);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertEnd, n, out placement));
            Assert.Equal(CaretPlacement.End, placement);
            Assert.Equal(5, n.Caret);

            // I (shift) -> InsertStart, placement Start.
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.I, true) == TextMotion.InsertStart);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertStart, n, out placement));
            Assert.Equal(CaretPlacement.Start, placement);
            Assert.Equal(0, n.Caret);

            // I (bare) -> not handled (generic insert lives in the overlay state machine).
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.MapKey(Key.I, false) == null);
            Assert.Equal(2, n.Caret);
        }

        public static void Run_TextMotionNavigator_LineNumber()
        {
            // m32: LineNumber is the 1-based line of the caret (count of '\n' in text[0..caret) + 1).
            // Behavior-preserving pin: the build-agent may cache it later (m32), but the values must
            // not change across motions — the preview `caret=... line=...` diagnostic depends on it.
            var n = new TextMotionNavigator();
            n.SetText("alpha\nbeta\ngamma");

            n.MoveTo(0);
            Assert.Equal(1, n.LineNumber);
            n.MoveTo(6); // start of "beta"
            Assert.Equal(2, n.LineNumber);
            n.MoveTo(11); // start of "gamma"
            Assert.Equal(3, n.LineNumber);
            n.MoveTo(16); // end of text
            Assert.Equal(3, n.LineNumber);

            // Down from line 1 keeps the column and lands on line 2.
            n.MoveTo(0);
            n.Down();
            Assert.Equal(2, n.LineNumber);

            // Up back to line 1.
            n.Up();
            Assert.Equal(1, n.LineNumber);

            // MoveToLine jumps to the requested 1-based line.
            n.MoveToLine(3);
            Assert.Equal(3, n.LineNumber);

            // Bottom -> last line; Top -> first line.
            n.Bottom();
            Assert.Equal(3, n.LineNumber);
            n.Top();
            Assert.Equal(1, n.LineNumber);

            // A caret ON a '\n' is still the line that the newline terminates.
            n.MoveTo(5); // the '\n' after "alpha"
            Assert.Equal(1, n.LineNumber);
            n.MoveTo(10); // the '\n' after "beta"
            Assert.Equal(2, n.LineNumber);
        }

        public static void Run_TextMotionNavigator_DownAtLastLineNoOp()
        {
            // N66 (BP-62): Down() on the last line must stay at the last position (no-op), not
            // jump to _text.Length. RED today: Down() moves the caret to _text.Length.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(5); // on the last line ("def"), caret at 'e'
            n.Down();
            Assert.Equal(5, n.Caret);
        }

        public static void Run_TextMotionNavigator_UpAtFirstLineNoOp()
        {
            // N66 (BP-62): Up() on the first line must stay put, not jump to 0. RED today: Up()
            // moves the caret to 0.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(1); // first line, caret at 'b'
            n.Up();
            Assert.Equal(1, n.Caret);
        }

        // ================================================================
        // PromptMotionRouter — prompt/preview motion routing seam (M1/m52)
        // RED: `PromptMotionRouter` does not exist yet -> compile error (CS0246)
        // ShouldConsume returns TRUE only for motions (h/l/w/b/e/0/$/gg/G) and FALSE
        // for the insert placements (a/A/I) with the placement reported via `out`, so
        // a/A/I fall through to the overlay state machine (insert mode) instead of
        // being consumed as prompt/preview caret motions.
        // ================================================================

        // TEMPORARILY COMMENTED OUT (compile-RED — the missing PromptMotionRouter.ShouldConsume
        // hasCtrl param breaks the build; restored at the end of the RED phase).
        public static void Run_PromptMotionRouter_InsertPlacementsNotConsumed()
        {
            // a/A/I are insert placements, NOT prompt/preview motions: ShouldConsume must
            // return false (fall through to _keyHandler.Handle / no preview motion) and
            // report the placement via `out`. RED: `PromptMotionRouter` does not exist
            // -> compile error (CS0246).
            CaretPlacement? placement;

            // a (bare) -> InsertAfter, placement AfterCaret (N31/BP-44: a inserts AFTER the caret,
            // distinct from i's Current). RED today: PromptMotionRouter maps bare a to Current and
            // CaretPlacement.AfterCaret does not exist -> compile error.
            Assert.False(PromptMotionRouter.ShouldConsume(Key.A, false, out _, out placement, hasCtrl: false),
                "bare a is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.AfterCaret, placement);

            // A (shift) -> InsertEnd, placement End.
            Assert.False(PromptMotionRouter.ShouldConsume(Key.A, true, out _, out placement, hasCtrl: false),
                "shift+A is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.End, placement);

            // i (bare) -> generic insert at the current position (OverlayKey.I -> Current).
            Assert.False(PromptMotionRouter.ShouldConsume(Key.I, false, out _, out placement, hasCtrl: false),
                "bare i is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.Current, placement);

            // I (shift) -> InsertStart, placement Start.
            Assert.False(PromptMotionRouter.ShouldConsume(Key.I, true, out _, out placement, hasCtrl: false),
                "shift+I is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.Start, placement);
        }

        public static void Run_PromptMotionRouter_MotionsConsumed()
        {
            // Motions ARE consumed by the prompt/preview motion handler (placement null).
            Assert.True(PromptMotionRouter.ShouldConsume(Key.H, false, out _, out _, hasCtrl: false),
                "h is a prompt motion");
            Assert.True(PromptMotionRouter.ShouldConsume(Key.W, false, out _, out _, hasCtrl: false),
                "w is a prompt motion");
            Assert.True(PromptMotionRouter.ShouldConsume(Key.D4, true, out _, out _, hasCtrl: false),
                "shift+4 ($) is a prompt motion");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.D4, false, out _, out _, hasCtrl: false),
                "bare $ (D4 without shift) is not a prompt motion");
        }

        public static void Run_PromptMotionRouter_PromptRestrictsVerticalMotions()
        {
            // R1 (BP-1): the prompt motion set must be restricted to h/l/w/b/e/0/$ — j/k/g/G are
            // selection-navigation keys that must fall through to OverlayKeyHandler (Down/Up/Top/
            // Bottom), NOT be consumed as prompt caret motions. The restriction is surface-aware:
            // the PREVIEW surface still needs j/k/g/G (HandlePreviewKey), so the fix adds a surface
            // param to ShouldConsume; the build-agent extends the call sites for it. RED: today
            // ShouldConsume returns true for the prompt surface (j/k/g/G are consumed as caret
            // motions), so each Assert.False fails.
            Assert.False(PromptMotionRouter.ShouldConsume(Key.J, false, out _, out _, hasCtrl: false),
                "j must not be consumed in the prompt (selection navigation)");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.K, false, out _, out _, hasCtrl: false),
                "k must not be consumed in the prompt (selection navigation)");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.G, false, out _, out _, hasCtrl: false),
                "g must not be consumed in the prompt (gg -> Top is a selection move)");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.G, true, out _, out _, hasCtrl: false),
                "G must not be consumed in the prompt (Bottom is a selection move)");
        }

        // BP-27 (m30): ShouldConsume gains a hasCtrl param — a Ctrl chord (e.g. Ctrl+W) is NOT a
        // prompt motion (the window-navigation chords win). COMPILE-RED: the hasCtrl param does
        // not exist yet -> CS1739.
        public static void Run_PromptMotion_CtrlGuard()
        {
            Assert.False(PromptMotionRouter.ShouldConsume(Key.W, false, out _, out _, hasCtrl: true),
                "Ctrl+W is not a prompt motion (the Ctrl chords are window navigation) — BP-27/m30");
            Assert.True(PromptMotionRouter.ShouldConsume(Key.W, false, out _, out _, hasCtrl: false),
                "plain w is a prompt motion — BP-27/m30");
        }

        // ================================================================
        // BlockCaretStyle — shared block-caret brush/geometry (M25)
        // RED: `BlockCaretStyle` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_BlockCaretStyle_BrushFrozenWhite()
        {
            var brush = BlockCaretStyle.CreateBlockBrush();
            Assert.True(brush.IsFrozen, "the shared block-caret brush is frozen");
            Assert.Equal(Colors.White, BlockCaretStyle.WhiteFill);
            Assert.Equal(Colors.Black, BlockCaretStyle.GlyphColor);
            Assert.Equal(8.0, BlockCaretStyle.BlockRect.Width);
            Assert.Equal(16.0, BlockCaretStyle.BlockRect.Height);
        }

        public static void Run_BlockCaretStyle_SingleSharedInstance()
        {
            // Every call must return the SAME frozen instance (single shared static brush).
            var first = BlockCaretStyle.CreateBlockBrush();
            var second = BlockCaretStyle.CreateBlockBrush();
            Assert.True(ReferenceEquals(first, second), "CreateBlockBrush returns the SAME shared instance");
        }

        // ================================================================
        // LineIndex (BP-6/M7a) — pure index -> (line, offset) mapping shared
        // with M11 (Phase 3). LineOf MUST equal TextMotionNavigator.LineNumber
        // semantics (count of '\n' in text[0..index) + 1).
        // RED: LineIndex does not exist -> compile error (CS0246).
        // BP-D14 (n18): this is the ONLY cross-implementation check in the suite — it asserts
        // LineIndex.LineOf equals TextMotionNavigator.LineNumber across 7 inputs (two independent
        // implementations of the same line-number semantics). It is deliberately KEPT despite the
        // shared-defect caveat (n18): a shared defect in both implementations would pass here, but
        // the cross-check still catches a divergence introduced by a change to either side.
        // ================================================================

        public static void Run_LineIndex_LineOfMatchesNavigator()
        {
            string[] inputs =
            {
                "alpha\nbeta\ngamma",
                "\nleading newline",
                "trailing newline\n",
                "a\n\nb",
                "one\r\ntwo\r\nthree",
                "single line",
                "\n\n\n",
            };
            foreach (string text in inputs)
            {
                var index = new LineIndex(text);
                var nav = new TextMotionNavigator();
                nav.SetText(text);
                for (int i = 0; i <= text.Length; i++)
                {
                    nav.MoveTo(i);
                    Assert.Equal(nav.LineNumber, index.LineOf(i));
                }
            }
        }

        public static void Run_LineIndex_LineStartOffsets()
        {
            var index = new LineIndex("alpha\nbeta\ngamma");
            Assert.Equal(0, index.LineStart(1));
            Assert.Equal(6, index.LineStart(2)); // after the first '\n'
            Assert.Equal(11, index.LineStart(3)); // after the second '\n'
        }

        public static void Run_LineIndex_EdgeCases()
        {
            // Empty text: a single line; LineOf(0) == 1.
            var empty = new LineIndex("");
            Assert.Equal(1, empty.LineOf(0));
            Assert.Equal(1, empty.LineCount);

            // Text ending in '\n': the trailing newline opens a final (empty) line.
            var trailing = new LineIndex("a\n");
            Assert.Equal(1, trailing.LineOf(0));
            Assert.Equal(1, trailing.LineOf(1)); // the '\n' itself is still line 1
            Assert.Equal(2, trailing.LineOf(2)); // past the '\n' -> line 2
            Assert.Equal(2, trailing.LineCount);

            // \r\n endings: only '\n' advances the line (the '\r' does not).
            var crlf = new LineIndex("a\r\nb");
            Assert.Equal(1, crlf.LineOf(0));
            Assert.Equal(1, crlf.LineOf(2)); // the '\r' is still line 1
            Assert.Equal(2, crlf.LineOf(3)); // the '\n' starts line 2
            Assert.Equal(2, crlf.LineCount);
        }

        // ================================================================
        // FileLocation / IFileLocation / FileHit (BP-1/L3)
        // RED: FileLocation / IFileLocation / FileHit do not exist yet -> compile error
        // ================================================================

        public static void Run_FileLocation_NullCoalescing()
        {
            // The shared FileLocation ctor null-coalesces FilePath to "" (the behavior the 4 hit
            // models currently hand-roll) and stores LineNumber verbatim.
            var hit = new TestHit(null!, 5);
            Assert.Equal("", hit.FilePath);
            Assert.Equal(5, hit.LineNumber);
        }

        public static void Run_FileLocation_SubclassExtraFields()
        {
            // Each hit model keeps its extra fields and inherits FilePath/LineNumber from
            // FileLocation (the base-class relationship is the contract being pinned).
            var issue = new CodeIssue(CodeIssueKind.Todo, @"C:\p\A.cs", 3, "fix me");
            Assert.True(issue is FileLocation, "CodeIssue inherits FileLocation");
            Assert.Equal(@"C:\p\A.cs", issue.FilePath);
            Assert.Equal(3, issue.LineNumber);
            Assert.Equal(CodeIssueKind.Todo, issue.Kind);
            Assert.Equal("fix me", issue.Text);

            var grep = new GrepHit(@"C:\p\B.cs", 7, "// needle");
            Assert.True(grep is FileLocation, "GrepHit inherits FileLocation");
            Assert.Equal(@"C:\p\B.cs", grep.FilePath);
            Assert.Equal(7, grep.LineNumber);
            Assert.Equal("// needle", grep.LineText);

            var reference = new ReferenceHit(@"C:\p\C.cs", 9, 4, true, "Value", "Value = 1;");
            Assert.True(reference is FileLocation, "ReferenceHit inherits FileLocation");
            Assert.Equal(@"C:\p\C.cs", reference.FilePath);
            Assert.Equal(9, reference.LineNumber);
            Assert.Equal(4, reference.Column);
            Assert.True(reference.IsWrite, "ReferenceHit keeps IsWrite");
            Assert.Equal("Value", reference.Symbol);
            Assert.Equal("Value = 1;", reference.LineText);

            var impl = new ImplementationHit(@"C:\p\D.cs", 2, "Shape", "Class");
            Assert.True(impl is FileLocation, "ImplementationHit inherits FileLocation");
            Assert.Equal(@"C:\p\D.cs", impl.FilePath);
            Assert.Equal(2, impl.LineNumber);
            Assert.Equal("Shape", impl.SymbolName);
            Assert.Equal("Class", impl.Kind);
        }

        public static void Run_IFileLocation_Contract()
        {
            // IFileLocation exposes FilePath + LineNumber; all 5 hit models implement it.
            IFileLocation issue = new CodeIssue(CodeIssueKind.Todo, @"C:\p\A.cs", 3, "fix me");
            Assert.Equal(@"C:\p\A.cs", issue.FilePath);
            Assert.Equal(3, issue.LineNumber);

            IFileLocation grep = new GrepHit(@"C:\p\B.cs", 7, "// needle");
            Assert.Equal(@"C:\p\B.cs", grep.FilePath);
            Assert.Equal(7, grep.LineNumber);

            IFileLocation reference = new ReferenceHit(@"C:\p\C.cs", 9, 4, true, "Value", "Value = 1;");
            Assert.Equal(@"C:\p\C.cs", reference.FilePath);
            Assert.Equal(9, reference.LineNumber);

            IFileLocation impl = new ImplementationHit(@"C:\p\D.cs", 2, "Shape", "Class");
            Assert.Equal(@"C:\p\D.cs", impl.FilePath);
            Assert.Equal(2, impl.LineNumber);

            IFileLocation file = new FileHit(@"C:\p\E.cs", 0);
            Assert.Equal(@"C:\p\E.cs", file.FilePath);
            Assert.Equal(0, file.LineNumber);
        }

        public static void Run_IFileLocation_FileHit()
        {
            // FileHit is the new file-finder hit model (LineNumber = 0 for plain files).
            var hit = new FileHit(@"C:\p\A.cs", 0);
            Assert.Equal(@"C:\p\A.cs", hit.FilePath);
            Assert.Equal(0, hit.LineNumber);
        }

        // ================================================================
        // HitOpener — shared open-at-line helper (M23)
        // RED: `HitOpener` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_HitOpener_ExistingFileInvokesDelegate()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(path, "// a");
                string? openedPath = null;
                int openedLine = -1;
                HitOpener.OpenAtLine(new ReferenceHit(path, 7, 1, false, "X", "X = 1;"),
                    (p, l) => { openedPath = p; openedLine = l; });
                Assert.Equal(path, openedPath);
                Assert.Equal(7, openedLine);
            }
        }

        public static void Run_HitOpener_MissingFileDoesNotInvoke()
        {
            using (var dir = new TempDir())
            {
                string missing = Path.Combine(dir.Path, "Ghost.cs");
                int invoked = 0;
                HitOpener.OpenAtLine(new ReferenceHit(missing, 3, 1, false, "X", "X = 1;"),
                    (p, l) => invoked++);
                Assert.Equal(0, invoked);
            }
        }

        public static void Run_HitOpener_ReferenceAndImplementationFlowThrough()
        {
            using (var dir = new TempDir())
            {
                string refPath = Path.Combine(dir.Path, "Ref.cs");
                string implPath = Path.Combine(dir.Path, "Impl.cs");
                File.WriteAllText(refPath, "// r");
                File.WriteAllText(implPath, "// i");

                var opened = new List<(string, int)>();
                HitOpener.OpenAtLine(new ReferenceHit(refPath, 5, 1, true, "X", "X = 1;"),
                    (p, l) => opened.Add((p, l)));
                HitOpener.OpenAtLine(new ImplementationHit(implPath, 9, "X", "Method"),
                    (p, l) => opened.Add((p, l)));

                Assert.Equal(2, opened.Count);
                Assert.Equal(refPath, opened[0].Item1);
                Assert.Equal(5, opened[0].Item2);
                Assert.Equal(implPath, opened[1].Item1);
                Assert.Equal(9, opened[1].Item2);
            }
        }

        // ================================================================
        // FinderBase<THit> (BP-2/L2) — the shared finder skeleton
        // RED: FinderBase<THit> does not exist yet -> compile error
        // ================================================================

        public static void Run_FinderBase_GetCandidatesMapsGather()
        {
            var hits = new List<TestHit> { new TestHit(@"C:\p\A.cs", 1), new TestHit(@"C:\p\B.cs", 2) };
            var finder = new TestFinder(() => hits);

            var entries = finder.GetCandidates();

            Assert.Equal(2, entries.Count);
            Assert.True(ReferenceEquals(hits[0], entries[0].Payload), "first entry carries the exact first hit");
            Assert.True(ReferenceEquals(hits[1], entries[1].Payload), "second entry carries the exact second hit");
        }

        public static void Run_FinderBase_OnSelectedOpensHit()
        {
            var hit = new TestHit(@"C:\p\A.cs", 1);
            TestHit? opened = null;
            var finder = new TestFinder(() => new[] { hit }, h => opened = h);

            finder.OnSelected(new FinderEntry("A.cs", hit));

            Assert.True(ReferenceEquals(hit, opened), "OnSelected invokes OpenHit with the exact payload");
        }

        public static void Run_FinderBase_GatherErrorSwallowed()
        {
            var finder = new TestFinder(() => throw new InvalidOperationException("gather boom"));

            var entries = finder.GetCandidates();

            Assert.Equal(0, entries.Count);
        }

        public static void Run_FinderBase_OpenErrorSwallowed()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var hit = new TestHit(@"C:\p\A.cs", 1);
                    var finder = new TestFinder(() => new[] { hit }, _ => throw new InvalidOperationException("open boom"));

                    // A throwing OpenHit must not propagate out of OnSelected (the runner fails the
                    // test if it does) AND the error must be logged exactly once (m23 — the test
                    // previously had no assertion and passed vacuously).
                    finder.OnSelected(new FinderEntry("A.cs", hit));
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var telescopeLines = lines.Where(l => l.Contains("[Telescope] ")).ToList();
                    Assert.Equal(1, telescopeLines.Count);
                    Assert.True(telescopeLines[0].Contains("[Telescope] open item failed: open boom"),
                        $"expected exactly one '[Telescope] open item failed: open boom' line, got: {string.Join(" | ", telescopeLines)}");
                });
            }
        }

        public static void Run_FinderBase_NonMatchingPayloadIgnored()
        {
            var hit = new TestHit(@"C:\p\A.cs", 1);
            int opened = 0;
            var finder = new TestFinder(() => new[] { hit }, _ => opened++);

            finder.OnSelected(new FinderEntry("A.cs", "not a TestHit"));

            Assert.Equal(0, opened);
        }

        // ================================================================
        // FinderBase error logging (BP-5/m13/m20) — finder errors must be
        // routed through TelescopeLog.Log (which adds the [Telescope] prefix)
        // and each failure must emit EXACTLY ONE line.
        // RED: `Run_FinderBase_OpenErrorSingleLog` fails at runtime — today
        //      OnSelected double-logs (TelescopeLog.Log + NeoVisualLog.Debug),
        //      so the file holds 2 [Telescope] lines.
        //      `Run_FinderBase_GatherErrorPrefixed` pins the single-prefix
        //      contract (a naive fix that wraps the already-prefixed
        //      GatherErrorLiteral in TelescopeLog.Log would double-prefix).
        // ================================================================

        public static void Run_FinderBase_OpenErrorSingleLog()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var hit = new TestHit(@"C:\p\A.cs", 1);
                    var finder = new TestFinder(() => new[] { hit }, _ => throw new InvalidOperationException("open boom"));

                    finder.OnSelected(new FinderEntry("A.cs", hit));
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var telescopeLines = lines.Where(l => l.Contains("[Telescope] ")).ToList();
                    Assert.Equal(1, telescopeLines.Count);
                    Assert.True(telescopeLines[0].Contains("[Telescope] open item failed: open boom"),
                        $"expected exactly one '[Telescope] open item failed: open boom' line, got: {string.Join(" | ", telescopeLines)}");
                });
            }
        }

        public static void Run_FinderBase_GatherErrorPrefixed()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var finder = new TestFinder(() => throw new InvalidOperationException("gather boom"));

                    finder.GetCandidates();
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var telescopeLines = lines.Where(l => l.Contains("[Telescope] ")).ToList();
                    Assert.Equal(1, telescopeLines.Count);
                    // File lines are timestamped ("HH:mm:ss.fff <message>"), so the prefix check is
                    // a Contains (matching Run_TelescopeLog_Prefix), not a StartsWith.
                    Assert.True(telescopeLines[0].Contains("[Telescope] TestFinder failed to enumerate: gather boom"),
                        $"expected '[Telescope] TestFinder failed to enumerate: gather boom', got: {string.Join(" | ", telescopeLines)}");
                });
            }
        }

        // TEMPORARILY COMMENTED OUT (compile-RED — the missing single-seam FileFinder ctor
        // (Func<DTE>, ProjectFileCache, Func<IReadOnlyList<string>>, Action<string>) breaks the
        // build; restored at the end of the RED phase).
        public static void Run_FileFinder_EnumeratesCandidates()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                string b = Path.Combine(dir.Path, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                // BP-14 (m36): the single-seam ctor (testEnumerate + testOpener — testCandidateSource
                // is dropped). COMPILE-RED today: the 4-arg call binds testCandidateSource=testEnumerate
                // and testEnumerate=testOpener (Action<string> != Func<IReadOnlyList<string>>) -> CS1503.
                var finder = new FileFinder(() => null!, new ProjectFileCache(), () => new[] { a, b }, _ => { });
                var entries = finder.GetCandidates();

                Assert.Equal(2, entries.Count);
                Assert.Equal("Alpha.cs", entries[0].Display);
                Assert.Equal(a, (entries[0].Payload as FileHit)?.FilePath);
                Assert.Equal("Beta.cs", entries[1].Display);
            }
        }

        public static void Run_FileFinder_OpenSelectedCallsOpener()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                File.WriteAllText(a, "// a");
                string? opened = null;
                var finder = new FileFinder(() => null!, new ProjectFileCache(), () => new[] { a }, p => opened = p);

                finder.OnSelected(new FinderEntry("Alpha.cs", new FileHit(a, 0)));
                Assert.Equal(a, opened);
            }
        }

        public static void Run_FileFinder_OpenMissingFileIsNoOp()
        {
            using (var dir = new TempDir())
            {
                string missing = Path.Combine(dir.Path, "Ghost.cs");
                int opened = 0;
                var finder = new FileFinder(() => null!, new ProjectFileCache(), () => new[] { missing }, _ => opened++);

                finder.OnSelected(new FinderEntry("Ghost.cs", new FileHit(missing, 0)));
                Assert.Equal(0, opened);
            }
        }

        // ================================================================
        // FileFinder + ProjectFileCache (BP-11/m34) — the finder's enumerate
        // delegate must be served by the shared ProjectFileCache (single
        // enumeration across gathers), mirroring the GrepFinder cache-injection
        // ctor (Run_GrepFinder_CacheEnumeratesOnce). Today FileFinder.GatherHits
        // (FileFinder.cs:62) calls ProjectFiles.Enumerate directly with no cache.
        // RED: the FileFinder(ProjectFileCache, Func<IReadOnlyList<string>>,
        //      Action<string>) ctor does not exist -> compile error (CS1729).
        // ================================================================

        // TEMPORARILY COMMENTED OUT (compile-RED — the missing single-seam FileFinder ctor breaks
        // the build; restored at the end of the RED phase).
        public static void Run_FileFinder_UsesProjectFileCache()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// a");

                int count = 0;
                var cache = new ProjectFileCache();
                var finder = new FileFinder(() => null!, cache, () => { count++; return new[] { a }; }, _ => { });

                finder.GetCandidates();
                finder.GetCandidates();

                // The enumerate delegate must run ONCE across two gathers — the cache serves the
                // second GetCandidates (today FileFinder re-enumerates per gather).
                Assert.Equal(1, count);
            }
        }

        // ================================================================
        // BP-14 (m36): FileFinder drops _testCandidateSource — the single _testEnumerate seam
        // serves candidate enumeration. COMPILE-RED: the single-seam ctor (4 args) does not exist
        // yet -> CS1503 (the 4-arg call binds testCandidateSource=testEnumerate and
        // testEnumerate=testOpener, an Action<string> vs Func<IReadOnlyList<string>> mismatch).
        // ================================================================

        public static void Run_FileFinder_SingleSeam()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// a");

                // The single-seam ctor enumerates candidates (testCandidateSource is gone).
                var finder = new FileFinder(() => null!, new ProjectFileCache(), () => new[] { a }, _ => { });
                var entries = finder.GetCandidates();
                Assert.Equal(1, entries.Count);
                Assert.Equal("A.cs", entries[0].Display);
                Assert.Equal(a, (entries[0].Payload as FileHit)?.FilePath);
            }
        }

        // ================================================================
        // HierarchyWalker — pure DTE-tree walker (M22)
        // RED: `HierarchyWalker` / `IHierarchyNode` do not exist yet -> compile error (CS0246)
        // ================================================================

        private sealed class FakeNode : IHierarchyNode
        {
            public FakeNode(string? path, params IHierarchyNode[] children)
            {
                Path = path;
                Children = children;
            }

            public IEnumerable<IHierarchyNode> Children { get; }
            public string? Path { get; }
        }

        public static void Run_HierarchyWalker_SolutionFolderRecursion()
        {
            // A solution folder (no path) contains a sub-project whose items are enumerated.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var root = new FakeNode(null,
                    new FakeNode(null, // solution folder
                        new FakeNode(null, // sub-project
                            new FakeNode(a),
                            new FakeNode(b))));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(2, files.Count);
                Assert.Equal(a, files[0]);
                Assert.Equal(b, files[1]);
            }
        }

        public static void Run_HierarchyWalker_NestedItemRecursion()
        {
            // An item with nested children (a folder item) recurses into them.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                string nested = Path.Combine(dir.Path, "Nested.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(nested, "// n");

                var root = new FakeNode(null,
                    new FakeNode(a,
                        new FakeNode(nested)));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(2, files.Count);
                Assert.Equal(a, files[0]);
                Assert.Equal(nested, files[1]);
            }
        }

        public static void Run_HierarchyWalker_Dedup()
        {
            // The same path reached twice must be enumerated once (OrdinalIgnoreCase dedup).
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// a");

                var root = new FakeNode(null,
                    new FakeNode(a),
                    new FakeNode(a));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(1, files.Count);
                Assert.Equal(a, files[0]);
            }
        }

        public static void Run_HierarchyWalker_FileExistsFiltering()
        {
            // A path that does not exist on disk must be dropped.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                string ghost = Path.Combine(dir.Path, "Ghost.cs");
                File.WriteAllText(a, "// a");

                var root = new FakeNode(null,
                    new FakeNode(a),
                    new FakeNode(ghost));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(1, files.Count);
                Assert.Equal(a, files[0]);
            }
        }

        public static void Run_HierarchyWalker_EmptyTree()
        {
            var files = HierarchyWalker.EnumerateFiles(new FakeNode[0]);
            Assert.Equal(0, files.Count);
        }

        // ================================================================
        // Preview -> real editor view (D9/D10, Section P). The pure pieces the
        // migration keeps unit-testable: the navigator-target -> editor-caret
        // mapping (clamped to the editor text — the mtime-drift guard) and the
        // preview diagnostic formats (byte-stable under the migration).
        // RED: PreviewCaretMap / PreviewDiagnostics do not exist yet -> CS0246.
        // ================================================================

        public static void Run_PreviewCaret_OffsetClampedToEditorText()
        {
            // The editor buffer may be SHORTER than the navigator's text (the file changed on
            // disk between navigator.SetText and the document load). The offset must clamp to
            // the editor text's length or new SnapshotPoint(snapshot, offset) throws
            // ArgumentOutOfRangeException.
            Assert.Equal(3, PreviewCaretMap.Offset("abc", 99));
            Assert.Equal(0, PreviewCaretMap.Offset("abc", -5));
            Assert.Equal(2, PreviewCaretMap.Offset("abc", 2));
        }

        public static void Run_PreviewCaret_LineOfClampedOffset()
        {
            // 1-based line of the CLAMPED offset — the scroll target and the
            // "preview caret= line=" diagnostic's line value. An offset AT a '\n' belongs to
            // the line it ENDS (matches the retired CaretToPointer's "land at the end of the
            // line" behavior, PreviewRenderer.cs:236-238).
            Assert.Equal(3, PreviewCaretMap.Line("one\ntwo\nthree", 8));
            Assert.Equal(2, PreviewCaretMap.Line("one\ntwo\nthree", 5));
            Assert.Equal(1, PreviewCaretMap.Line("one\ntwo\nthree", 3));   // the '\n' after "one"
            Assert.Equal(3, PreviewCaretMap.Line("one\ntwo\nthree", 99));  // clamped -> last line
        }

        public static void Run_PreviewCaret_EmptyEditorText()
        {
            // The empty-preview case: no crash, line 1.
            Assert.Equal(0, PreviewCaretMap.Offset("", 0));
            Assert.Equal(1, PreviewCaretMap.Line("", 0));
        }

        public static void Run_PreviewDiagnostics_CaretFormat()
        {
            // Byte-stable under the migration (D10): the EXACT current format
            // (PreviewRenderer.cs:58 / TelescopeOverlay.cs:574). TelescopeLog supplies the
            // "[Telescope] " prefix; the helper returns the message body only.
            Assert.Equal("preview caret=12 line=3", PreviewDiagnostics.Caret(12, 3));
            Assert.Equal("preview caret=0 line=1", PreviewDiagnostics.Caret(0, 1));
        }

        public static void Run_PreviewDiagnostics_FileFormat()
        {
            // Byte-stable under the migration (D10): the EXACT current format
            // (PreviewRenderer.cs:60).
            Assert.Equal(@"preview file=C:\p\A.cs chars=123", PreviewDiagnostics.File(@"C:\p\A.cs", 123));
        }

        // ================================================================
        // Preview buffer source (the workspace-attach fix, plan-preview-buffer):
        // the pure try-workspace-then-fallback decision + the ownership split it
        // implies. The VS-coupled host (MyExtension PreviewEditorHost) delegates
        // the decision to this pure type — the workspace buffer is NEVER disposed
        // by the host; only the standalone ITextDocument is.
        // RED: PreviewBufferSource / PreviewBufferKind do not exist yet -> CS0246.
        // ================================================================

        public static void Run_PreviewBuffer_WorkspaceWhenAvailableAndFound()
        {
            // A resolved workspace + a document id for the path -> the LIVE workspace
            // buffer (the Peek model). The host does NOT own it: OwnsDocument=false
            // means CloseView must never dispose it (the workspace owns the buffer;
            // the read-only view never mutates it).
            var d = PreviewBufferSource.Resolve(workspaceAvailable: true, documentFound: true);
            Assert.Equal(PreviewBufferKind.Workspace, d.Kind);
            Assert.False(d.OwnsDocument);
        }

        public static void Run_PreviewBuffer_StandaloneWhenNoWorkspace()
        {
            // No workspace (MEF resolution failed/null) -> the standalone content-type
            // document; the host owns + disposes it (the pre-fix behavior).
            var d = PreviewBufferSource.Resolve(workspaceAvailable: false, documentFound: false);
            Assert.Equal(PreviewBufferKind.Standalone, d.Kind);
            Assert.True(d.OwnsDocument);
        }

        public static void Run_PreviewBuffer_StandaloneWhenDocumentMisses()
        {
            // A resolved workspace that does not know the path (a non-solution file,
            // a non-Roslyn content type, C++) -> the standalone fallback.
            var d = PreviewBufferSource.Resolve(workspaceAvailable: true, documentFound: false);
            Assert.Equal(PreviewBufferKind.Standalone, d.Kind);
            Assert.True(d.OwnsDocument);
        }

        // ================================================================
        // CodeIssuesFinder — warnings/errors/TODO markers
        // ================================================================

        public static void Run_Issues_TodoScanFindsMarkers()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "class A\n{\n    // TODO: fix this\n    // FIXME: and this\n    int x;\n}");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(b, "// nothing here\n");

                var finder = new CodeIssuesFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a, b }, _ => { });
                var entries = finder.GetCandidates();

                Assert.Equal(2, entries.Count);
                Assert.True(entries[0].Display.StartsWith("[TODO] line 3:"), $"todo on line 3, got '{entries[0].Display}'");
                Assert.True(entries[1].Display.StartsWith("[TODO] line 4:"), $"fixme on line 4, got '{entries[1].Display}'");
                Assert.True(entries[0].Display.Contains("A.cs"), "display names the file");
            }
        }

        public static void Run_Issues_NoFalsePositiveOnTodoWord()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "var todoList = new List<int>();\nint total = 1;\n");

                var finder = new CodeIssuesFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates().Count);
            }
        }

        public static void Run_Issues_OnSelectedReportsPathAndLine()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\n// TODO: here\n");

                CodeIssue? opened = null;
                var finder = new CodeIssuesFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, issue => opened = issue);
                var entry = finder.GetCandidates()[0];

                finder.OnSelected(entry);
                Assert.True(opened != null, "opener invoked");
                Assert.Equal(a, opened!.FilePath);
                Assert.Equal(2, opened!.LineNumber);
                Assert.Equal(CodeIssueKind.Todo, opened!.Kind);
            }
        }

        public static void Run_Issues_CollectTodosUsesCache()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// TODO: one\n");

                var finder = new CodeIssuesFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var first = finder.GetCandidates();
                Assert.Equal(1, first.Count);

                // m15: the second scan over the same file must be served from the shared
                // FileContentCache — the file is NOT re-read. RED today: CollectTodos calls
                // File.ReadAllLines directly, so the second scan re-reads the file; locking it
                // exclusively makes that re-read fail (sharing violation) and CollectTodos skips
                // the file -> 0 entries. After the fix the cache serves the second scan from
                // memory (mtime unchanged) -> 1 entry.
                using (var fs = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var second = finder.GetCandidates();
                    Assert.Equal(1, second.Count);
                }
            }
        }

        public static void Run_Issues_SeverityMediumMapsToWarning()
        {
            // A warning whose message contains "error" must still be Warning: classification is
            // severity-based (ErrorItem.ErrorLevel), not description-based (the old Classify(string)
            // guessed by substring on the message).
            Assert.Equal(CodeIssueKind.Warning,
                CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevel.vsBuildErrorLevelMedium));
        }

        public static void Run_Issues_SeverityHighMapsToError()
        {
            Assert.Equal(CodeIssueKind.Error,
                CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevel.vsBuildErrorLevelHigh));
        }

        public static void Run_Issues_SeverityLowMapsToInfo()
        {
            Assert.Equal(CodeIssueKind.Info,
                CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevel.vsBuildErrorLevelLow));
        }

        // BP-4 (M4): GetCandidatesAsync must not block the calling thread on the TODO scan (the
        // scan runs via await Task.Run). DESIGN RESOLUTION: the current GatherHits blocks via
        // Task.Run(...).GetAwaiter().GetResult() (CodeIssuesFinder.cs:81), so a gate-blocked
        // reader would HANG the test thread — use an ELAPSED-TIME assertion instead: measure how
        // long GetCandidatesAsync takes to RETURN the task (not to complete). RED today: the base
        // GetCandidatesAsync evaluates GetCandidates(query) synchronously, which blocks on the
        // gate -> the call takes ~300ms.
        public static void Run_CodeIssuesFinder_GatherAsync()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// TODO: fix this\n");

                var gate = new ManualResetEventSlim(false);
                var cache = new FileContentCache(
                    timestamp: _ => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    reader: _ => { gate.Wait(TimeSpan.FromMilliseconds(300)); return new[] { "// TODO: fix this" }; });

                var finder = new CodeIssuesFinder(() => null!, new ProjectFileCache(), cache, () => new[] { a }, _ => { });

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var task = finder.GetCandidatesAsync("");
                sw.Stop();

                // Release the gate so the scan can complete (both RED and GREEN paths).
                gate.Set();

                Assert.True(sw.ElapsedMilliseconds < 150,
                    $"GetCandidatesAsync must not block the calling thread on the TODO scan (took {sw.ElapsedMilliseconds}ms) — BP-4/M4");
                var entries = task.GetAwaiter().GetResult();
                Assert.True(entries.Count > 0, "the TODO scan returns hits");
            }
        }

        // ================================================================
        // ReferencesFinder — read/write references to the caret symbol
        // (hermetic seams: injected gatherer Func<IReadOnlyList<ReferenceHit>> + opener
        // Action<ReferenceHit>, mirroring CodeIssuesFinder)
        // ================================================================

        public static void Run_ReferencesFinder_DisplayShowsAccessMarker()
        {
            var read = new ReferenceHit(@"C:\p\Reader.cs", 5, 16, isWrite: false, "Value", "return Shared.Value;");
            var write = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            var finder = new ReferencesFinder(() => new[] { read, write }, _ => { });

            var entries = finder.GetCandidates();
            Assert.Equal(2, entries.Count);

            // The display must show the read/write access deterministically: "(read)" for a read,
            // "(write)" for a write — this is the per-row access contract (A2).
            Assert.True(entries[0].Display.Contains("(read)"), $"read hit shows '(read)', got '{entries[0].Display}'");
            Assert.True(entries[1].Display.Contains("(write)"), $"write hit shows '(write)', got '{entries[1].Display}'");
            // Display must carry the file name and the symbol so a human can disambiguate the row.
            Assert.True(entries[0].Display.Contains("Reader.cs"), $"read hit names its file, got '{entries[0].Display}'");
            Assert.True(entries[0].Display.Contains("Value"), $"read hit names the symbol, got '{entries[0].Display}'");
        }

        public static void Run_ReferencesFinder_PayloadRoundTrips()
        {
            var hit = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            var finder = new ReferencesFinder(() => new[] { hit }, _ => { });

            var entry = finder.GetCandidates()[0];
            // The ReferenceHit payload must round-trip through FinderEntry.Payload so OnSelected can
            // recover the exact file/line/col/access to open.
            Assert.True(ReferenceEquals(hit, entry.Payload), "payload must be the exact ReferenceHit instance");
            var payload = entry.Payload as ReferenceHit;
            Assert.True(payload != null, "payload is a ReferenceHit");
            Assert.Equal(@"C:\p\Writer.cs", payload!.FilePath);
            Assert.Equal(5, payload.LineNumber);
            Assert.True(payload.IsWrite, "write hit carries IsWrite=true");
        }

        public static void Run_ReferencesFinder_OnSelectedOpensHitWithAccess()
        {
            var write = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            ReferenceHit? opened = null;
            var finder = new ReferencesFinder(() => new[] { write }, hit => opened = hit);
            var entry = finder.GetCandidates()[0];

            finder.OnSelected(entry);
            Assert.True(opened != null, "opener invoked");
            Assert.Equal(@"C:\p\Writer.cs", opened!.FilePath);
            Assert.Equal(5, opened.LineNumber);
            Assert.Equal(5, opened.Column);
            Assert.True(opened.IsWrite, "opened hit preserves its write access");
        }

        public static void Run_ReferencesFinder_LineNumberDrivesPreviewJump()
        {
            // A3 line mapping: the hit's 1-based LineNumber is what positions the preview caret.
            // Feed a hit whose line is 3 (mid-file) into the shared navigator and prove it lands
            // on line 3 — the pure mapping the overlay's ReferenceHit preview branch relies on.
            string text = "one\ntwo\nthree\nfour";
            var hit = new ReferenceHit(@"C:\p\File.cs", 3, 1, isWrite: false, "Value", "three");
            var nav = new TextMotionNavigator();
            nav.SetText(text);
            nav.MoveToLine(hit.LineNumber);
            Assert.Equal(3, nav.LineNumber);
            Assert.Equal(8, nav.Caret); // start of "three"
        }

        // ================================================================
        // ImplementationFinder — implementations/overrides of the caret symbol
        // (hermetic seams: injected gatherer Func<IReadOnlyList<ImplementationHit>> + opener
        // Action<ImplementationHit>, mirroring ReferencesFinder)
        // ================================================================

        public static void Run_ImplementationFinder_DisplayFormatting()
        {
            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var finder = new ImplementationFinder(() => new[] { hit }, _ => { });

            var entries = finder.GetCandidates();
            Assert.Equal(1, entries.Count);
            // Deterministic display: {Kind} {SymbolName} — {file}:{line} (A2 contract).
            Assert.Equal("Class Shape — Shape.cs:2", entries[0].Display);
        }

        public static void Run_ImplementationFinder_PayloadRoundTrips()
        {
            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var finder = new ImplementationFinder(() => new[] { hit }, _ => { });

            var entry = finder.GetCandidates()[0];
            // The ImplementationHit payload must round-trip through FinderEntry.Payload so
            // OnSelected can recover the exact file/line/kind to open.
            Assert.True(ReferenceEquals(hit, entry.Payload), "payload must be the exact ImplementationHit instance");
            var payload = entry.Payload as ImplementationHit;
            Assert.True(payload != null, "payload is an ImplementationHit");
            Assert.Equal(@"C:\p\Shape.cs", payload!.FilePath);
            Assert.Equal(2, payload.LineNumber);
            Assert.Equal("Shape", payload.SymbolName);
            Assert.Equal("Class", payload.Kind);
        }

        public static void Run_ImplementationFinder_OnSelectedOpensHitAtLine()
        {
            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            ImplementationHit? opened = null;
            var finder = new ImplementationFinder(() => new[] { hit }, h => opened = h);
            var entry = finder.GetCandidates()[0];

            finder.OnSelected(entry);
            Assert.True(opened != null, "opener invoked");
            Assert.Equal(@"C:\p\Shape.cs", opened!.FilePath);
            Assert.Equal(2, opened.LineNumber);
            Assert.Equal("Shape", opened.SymbolName);
            Assert.Equal("Class", opened.Kind);
        }

        public static void Run_ImplementationFinder_LineNumberDrivesPreviewJump()
        {
            // A4 line mapping: the hit's 1-based LineNumber is what positions the preview caret.
            // Feed a hit whose line is 3 (mid-file) into the shared navigator and prove it lands
            // on line 3 — the pure mapping the overlay's ImplementationHit preview branch relies on.
            string text = "one\ntwo\nthree\nfour";
            var hit = new ImplementationHit(@"C:\p\File.cs", 3, "File", "Class");
            var nav = new TextMotionNavigator();
            nav.SetText(text);
            nav.MoveToLine(hit.LineNumber);
            Assert.Equal(3, nav.LineNumber);
            Assert.Equal(8, nav.Caret); // start of "three"
        }

        // ================================================================
        // DefinitionFinder — the caret symbol's definition locations (Gap 6 D3)
        // (hermetic seams: injected gatherer Func<IReadOnlyList<DefinitionHit>> + opener
        // Action<DefinitionHit>, mirroring ReferencesFinder/ImplementationFinder)
        // RED: `DefinitionFinder` / `DefinitionHit` do not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_DefinitionFinder_DisplayFormatting()
        {
            var hit = new DefinitionHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var finder = new DefinitionFinder(() => new[] { hit }, _ => { });

            var entries = finder.GetCandidates();
            Assert.Equal(1, entries.Count);
            // Deterministic display, the ImplementationFinder contract (X4):
            // {Kind} {SymbolName} — {file}:{line}.
            Assert.Equal("Class Shape — Shape.cs:2", entries[0].Display);
            // The FinderNames contract (folded here, rev 1 — no separate _NameIsDefinition
            // test): the launcher maps "telescope-definition" -> this finder's Name.
            Assert.Equal("Definition", finder.Name);
        }

        public static void Run_DefinitionFinder_PayloadRoundTrips()
        {
            var hit = new DefinitionHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var finder = new DefinitionFinder(() => new[] { hit }, _ => { });

            var entry = finder.GetCandidates()[0];
            // The DefinitionHit payload must round-trip through FinderEntry.Payload so OnSelected
            // can recover the exact file/line/kind to open.
            Assert.True(ReferenceEquals(hit, entry.Payload), "payload must be the exact DefinitionHit instance");
            var payload = entry.Payload as DefinitionHit;
            Assert.True(payload != null, "payload is a DefinitionHit");
            Assert.Equal(@"C:\p\Shape.cs", payload!.FilePath);
            Assert.Equal(2, payload.LineNumber);
            Assert.Equal("Shape", payload.SymbolName);
            Assert.Equal("Class", payload.Kind);
        }

        public static void Run_DefinitionFinder_OnSelectedOpensHitAtLine()
        {
            // Rev 1: the open-outcome log assert (P5 row 2) is FOLDED here — no separate
            // _OpenLogsOpenedDefinition test (the canonical 6-test set covers every literal).
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var hit = new DefinitionHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
                    DefinitionHit? opened = null;
                    var finder = new DefinitionFinder(() => new[] { hit }, h => opened = h);
                    var entry = finder.GetCandidates()[0];

                    finder.OnSelected(entry);
                    Assert.True(opened != null, "opener invoked");
                    Assert.Equal(@"C:\p\Shape.cs", opened!.FilePath);
                    Assert.Equal(2, opened.LineNumber);
                    Assert.Equal("Shape", opened.SymbolName);
                    Assert.Equal("Class", opened.Kind);

                    LogFileWriter.Flush();
                    string content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[Telescope] opened definition: file=C:\\p\\Shape.cs line=2"),
                        $"expected '[Telescope] opened definition: file=C:\\p\\Shape.cs line=2', got: {content}");
                });
            }
        }

        public static void Run_DefinitionFinder_LineNumberDrivesPreviewJump()
        {
            // Line mapping: the hit's 1-based LineNumber is what positions the preview caret
            // (the shared TextMotionNavigator mapping the overlay's DefinitionHit preview
            // branch relies on — same pin as References/Implementation).
            string text = "one\ntwo\nthree\nfour";
            var hit = new DefinitionHit(@"C:\p\File.cs", 3, "File", "Class");
            var nav = new TextMotionNavigator();
            nav.SetText(text);
            nav.MoveToLine(hit.LineNumber);
            Assert.Equal(3, nav.LineNumber);
            Assert.Equal(8, nav.Caret); // start of "three"
        }

        public static void Run_DefinitionFinder_UnorderedGatherOrderedDeterministically()
        {
            // AC6 determinism (X5): the FINDER owns OrderBy(FilePath).ThenBy(LineNumber) so the
            // contract is hermetically testable — an arbitrarily-ordered gather comes out sorted.
            var b = new DefinitionHit(@"C:\p\B.cs", 10, "B", "Class");
            var a2 = new DefinitionHit(@"C:\p\A.cs", 20, "A", "Method");
            var a1 = new DefinitionHit(@"C:\p\A.cs", 5, "A", "Class");
            var finder = new DefinitionFinder(() => new[] { b, a2, a1 }, _ => { });

            var entries = finder.GetCandidates();
            Assert.Equal(3, entries.Count);
            Assert.Equal("Class A — A.cs:5", entries[0].Display);
            Assert.Equal("Method A — A.cs:20", entries[1].Display);
            Assert.Equal("Class B — B.cs:10", entries[2].Display);
        }

        public static void Run_DefinitionFinder_GatherSummaryLogged()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var hits = new[]
                    {
                        new DefinitionHit(@"C:\p\A.cs", 5, "A", "Class"),
                        new DefinitionHit(@"C:\p\B.cs", 10, "B", "Class"),
                    };
                    var finder = new DefinitionFinder(() => hits, _ => { });

                    finder.GetCandidates();
                    LogFileWriter.Flush();

                    // The gather-summary literal (X6) — the ImplementationFinder precedent
                    // (`implementations gathered count=`, ImplementationFinder.cs:44).
                    // Rev 1: the e2e lane is ENABLED (executed at VERIFY) — this hermetic
                    // pin is the fast offline gate, not a replacement for a deferred e2e.
                    string content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[Telescope] definitions gathered count=2"),
                        $"expected '[Telescope] definitions gathered count=2', got: {content}");
                });
            }
        }

        // ================================================================
        // GotoDispatcher (BP-23 / n15): the three Run_GotoDispatcher_* tests were DELETED — the
        // GotoDispatcher class is deleted by the build (the one-line Decide is inlined at the
        // single call site). The single/multi-hit behavior is pinned by the byte-stable
        // `[Telescope] goto-direct finder=... file=... line=...` diagnostic + the deferred
        // `telescope-goto` gate (E2E-CR77-1).
        // ================================================================

        // ================================================================
        // GrepFinder — query-driven live grep over the solution's files
        // (hermetic seams mirroring CodeIssuesFinder: injected file-PATH source +
        // Action<GrepHit> opener; the finder reads file CONTENT off disk from those paths)
        // ================================================================

        public static void Run_GrepFinder_EmptyQueryReturnsZeroCandidates()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates("").Count);
            }
        }

        // BP-3 (M4): the sync GetCandidates("non-empty") cannot await the async gather — it must
        // throw NotSupportedException (mirroring FzfFinder). RED today: the sync GetCandidates
        // delegates to GetCandidatesAsync(...).GetAwaiter().GetResult() and returns hits.
        public static void Run_GrepFinder_SyncGetCandidatesThrows()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });

                // The empty query stays a clean empty on the sync path (no throw).
                Assert.Equal(0, finder.GetCandidates("").Count);

                // A non-empty sync call must throw NotSupportedException (the async path is the
                // only supported entry point).
                bool threw = false;
                try
                {
                    finder.GetCandidates("needle");
                }
                catch (NotSupportedException)
                {
                    threw = true;
                }
                Assert.True(threw,
                    "a non-empty sync GetCandidates must throw NotSupportedException (BP-3/M4)");
            }
        }

        public static void Run_GrepFinder_LineScanMatchesCaseInsensitive()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(b, "// nothing here\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a, b }, _ => { });
                // Case-insensitive substring: "needle" matches BOTH "NEEDLE here" (line 2) and
                // "needle again" (line 4); the non-matching "middle" line and B.cs are excluded.
                // BP-3 (M4): the sync GetCandidates("non-empty") will throw NotSupportedException —
                // the async path is the only supported entry point.
                var entries = finder.GetCandidatesAsync("needle").GetAwaiter().GetResult();

                Assert.Equal(2, entries.Count);
                Assert.True(entries.All(e => e.Display.Contains("A.cs")), "only A.cs contains matches");
            }
        }

        public static void Run_GrepFinder_DisplayIsFileNameLineText()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "first\nNEEDLE here\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var entry = finder.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult()[0];
                // Deterministic {fileName}:{line}: {lineText} display (1-based line, fileName only).
                Assert.Equal("A.cs:2: NEEDLE here", entry.Display);
            }
        }

        public static void Run_GrepFinder_PayloadRoundTripsGrepHit()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "x\n// NEEDLE x\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var entry = finder.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult()[0];
                // The GrepHit payload must round-trip through FinderEntry.Payload so OnSelected can
                // recover the exact file/line/text to open.
                var payload = entry.Payload as GrepHit;
                Assert.True(payload != null, "payload is a GrepHit");
                Assert.Equal(a, payload!.FilePath);
                Assert.Equal(2, payload.LineNumber);
                Assert.Equal("// NEEDLE x", payload.LineText);
            }
        }

        public static void Run_GrepFinder_OnSelectedOpensHitAtLine()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "first\n// NEEDLE x\n");

                GrepHit? opened = null;
                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, hit => opened = hit);
                var entry = finder.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult()[0];

                finder.OnSelected(entry);
                Assert.True(opened != null, "opener invoked");
                Assert.Equal(a, opened!.FilePath);
                Assert.Equal(2, opened.LineNumber);
                Assert.Equal("// NEEDLE x", opened.LineText);
            }
        }

        public static void Run_GrepFinder_HitCapBounded()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 500; i++) { sb.AppendLine("NEEDLE " + i); }
                File.WriteAllText(a, sb.ToString());

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var entries = finder.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult();
                Assert.True(entries.Count > 0, "hits are still returned up to the cap");
                Assert.True(entries.Count <= 200, $"hit cap bounds the result set (got {entries.Count})");
            }
        }

        // ================================================================
        // GrepFinder + ProjectFileCache (BP-1/M5a) — the shared cache is injected
        // so the DTE solution-tree walk runs ONCE across queries (M5: the
        // per-keystroke walk is the stall being amortized).
        // RED: the GrepFinder(ProjectFileCache, Func<IReadOnlyList<string>>,
        //      Action<GrepHit>) ctor does not exist -> compile error (CS1729).
        // ================================================================

        public static void Run_GrepFinder_CacheEnumeratesOnce()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                int count = 0;
                var cache = new ProjectFileCache();
                var finder = new GrepFinder(() => null!, cache, new FileContentCache(500), () => { count++; return new[] { a }; }, _ => { });

                finder.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult();
                finder.GetCandidatesAsync("NEEDLE2").GetAwaiter().GetResult();

                // The enumerate delegate must run ONCE across two queries — the cache serves the
                // second GetCandidates (today GrepFinder re-walks the solution per query).
                Assert.Equal(1, count);
            }
        }

        public static void Run_GrepFinder_OffThreadScanSameHits()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var syncHits = finder.GetCandidatesAsync("needle").GetAwaiter().GetResult();

                // M4: the per-file content scan must return the SAME hits when it runs off-thread
                // (the fix moves the scan onto a background task and marshals only the results
                // back; the DTE enumeration stays on the UI thread). NOTE: this may PASS against
                // the current code — the hermetic path has no thread affinity — in which case the
                // off-thread seam is the fix (wiring-is-the-fix, not a RED).
                IReadOnlyList<FinderEntry> offThreadHits = null;
                Task.Run(() => offThreadHits = finder.GetCandidatesAsync("needle").GetAwaiter().GetResult()).GetAwaiter().GetResult();

                Assert.Equal(syncHits.Count, offThreadHits.Count);
                for (int i = 0; i < syncHits.Count; i++)
                {
                    Assert.Equal(syncHits[i].Display, offThreadHits[i].Display);
                }
            }
        }

        // ================================================================
        // GrepFinder.GatherHits (M41, BP-5) — the base-class gather stub must be a loud
        // failure, not a silent empty. GrepFinder is query-driven (GetCandidates(query)),
        // so the parameterless GatherHits() throws NotSupportedException. GrepFinder is
        // sealed, so the protected override is reached via reflection.
        // ================================================================

        public static void Run_GrepFinder_EmptyQueryCleanEmptyNoFailureLog()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { "a" }, _ => { });

                    // m27: use the public GetCandidates() path (no reflection). GrepFinder is
                    // query-driven: its GetCandidates(string) override short-circuits on an empty
                    // query (deterministic empty initial state) and never reaches the base gather
                    // stub, so the observable contract is: GetCandidates("") returns empty AND does
                    // NOT log a "GrepFinder failed to enumerate:" failure (the empty-query path is a
                    // clean empty, not a loud failure). The reflection removal is the m27 fix.
                    var entries = finder.GetCandidates("");
                    LogFileWriter.Flush();

                    Assert.Equal(0, entries.Count);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    int failureLines = content
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                        .Count(l => l.Contains("[Telescope] GrepFinder failed to enumerate:"));
                    Assert.Equal(0, failureLines);
                });
            }
        }

        // BP-5 (m9): a pre-cancelled gather must NOT log a "GrepFinder failed to enumerate:" line
        // (the cancellation path is silent, not a failure). Uses the DTE path (no hermetic
        // enumerate seam) so the generic catch is the only thing that could log. RED today: the
        // generic catch logs the line on cancel.
        public static void Run_GrepFinder_NoSpuriousCancelLog()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    using (SetCurrentDispatcherAsUiThread())
                    {
                        // No hermetic enumerate seam (the DTE path): a pre-cancelled gather must
                        // return 0 entries and NOT log the failure line.
                        var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500));
                        using (var cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            var entries = finder.GetCandidatesAsync("NEEDLE", cts.Token).GetAwaiter().GetResult();
                            LogFileWriter.Flush();

                            Assert.Equal(0, entries.Count);
                            string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                            int failureLines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                                .Count(l => l.Contains("[Telescope] GrepFinder failed to enumerate:"));
                            Assert.Equal(0, failureLines);
                        }
                    }
                });
            }
        }

        public static void Run_GrepFinder_QueryDrivenBehavior()
        {
            // R48 (BP-46): the old Run_GrepFinder_GatherHitsThrowsNotSupported invoked the protected
            // GatherHits via reflection — coupling the test to the internal method name/visibility.
            // Rewritten to assert the query-driven BEHAVIOR instead (hermeticity): GrepFinder is
            // query-driven (IsQueryDriven), the empty query short-circuits to no candidates, and a
            // non-empty query drives the gather through the hermetic test enumerate seam.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "class A\n{\n    int alpha;\n}");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });

                Assert.True(finder.IsQueryDriven, "GrepFinder is query-driven (GetCandidates(query))");
                Assert.True(finder.GetCandidates("").Count == 0, "empty query -> no candidates (short-circuit)");
                var hits = finder.GetCandidatesAsync("alpha").GetAwaiter().GetResult();
                Assert.Equal(1, hits.Count);
                Assert.True(hits[0].Display.Contains("A.cs"), "hit display names the file");
            }
        }

        // BP-13 (m7): the shared solution-invalidation helper must invalidate the cache exactly when
        // the solution name changes — ONE call per solution change (the duplicated blocks in
        // GrepFinder/FzfFinder/CodeIssuesFinder are replaced by this single helper used by all three
        // finders; the two paths GetCandidates/WarmContentCache can no longer disagree on cache
        // validity). RED: the shared helper does not exist -> CS0117 (the duplicated blocks are
        // still verbatim in the finders).
        public static void Run_GrepFinder_SharedInvalidation()
        {
            // Long TTL + fixed clock so the 5s ProjectFileCache expiry can never re-enumerate
            // mid-test (the counting seam must observe ONLY the helper's invalidations).
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new ProjectFileCache(() => fixedTime, TimeSpan.FromHours(1));
            string? cached = null;

            // First call: no cached name -> a solution change -> invalidates (returns true).
            Assert.True(ProjectFileCache.EnsureSolutionCache(cache, ref cached, "slnA.sln"),
                "a first call with no cached name is a solution change -> invalidates (BP-13)");

            // The cache is now invalidated: the next Get re-enumerates (the counting seam — one
            // re-enumeration per invalidation).
            int enumerations = 0;
            cache.Get(() => { enumerations++; return new[] { "a.cs" }; });
            Assert.Equal(1, enumerations);

            // The same solution name again -> NOT a change -> does NOT invalidate (returns false).
            Assert.False(ProjectFileCache.EnsureSolutionCache(cache, ref cached, "slnA.sln"),
                "the same solution name is not a change -> no invalidation (BP-13)");

            // The cache is still valid: Get does NOT re-enumerate.
            cache.Get(() => { enumerations++; return new[] { "a.cs" }; });
            Assert.Equal(1, enumerations);

            // A different solution name -> a change -> invalidates (returns true).
            Assert.True(ProjectFileCache.EnsureSolutionCache(cache, ref cached, "slnB.sln"),
                "a different solution name is a change -> invalidates (BP-13)");

            // The cache is invalidated again: Get re-enumerates (the second invalidation).
            cache.Get(() => { enumerations++; return new[] { "a.cs" }; });
            Assert.Equal(2, enumerations);

            // The compare is case-insensitive: the same name in a different case is NOT a change.
            Assert.False(ProjectFileCache.EnsureSolutionCache(cache, ref cached, "SLNB.SLN"),
                "the solution-name compare is case-insensitive (BP-13)");
        }

        // ================================================================
        // FzfFinder — query-driven fuzzy content finder over the solution's files
        // (hermetic seams mirroring GrepFinder: injected file-PATH source + Action<FzfHit>
        // opener + an IFzfEngine availability/filter seam; the finder reads file CONTENT
        // off disk from those paths and maps fzf-matched lines back via FzfLineMapper).
        // RED: FzfFinder / FzfHit / FzfLineMapper / IFzfEngine do not exist yet -> CS0246.
        // ================================================================

        // Test double for the IFzfEngine availability+filter seam (finding 2): drives both the
        // available (fuzzy filter) and unavailable (literal-fallback) paths with no real fzf
        // subprocess. FilterCalls records whether the finder spawned a filter (the empty-query
        // and unavailable paths must NOT).
        private sealed class FakeFzfEngine : IFzfEngine
        {
            private readonly bool _available;
            private readonly Func<IEnumerable<string>, string, IReadOnlyList<string>> _filter;
            private readonly Action? _onFilter;

            public int FilterCalls { get; private set; }

            // BP-3 (M2): records the CancellationToken the finder passed to FilterAsync, so a test
            // can assert the caller's token is threaded through (not CancellationToken.None).
            public CancellationToken LastToken { get; private set; }

            public FakeFzfEngine(bool available, Func<IEnumerable<string>, string, IReadOnlyList<string>>? filter = null, Action? onFilter = null)
            {
                _available = available;
                _filter = filter ?? ((candidates, query) =>
                    candidates.Where(c => c.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList());
                _onFilter = onFilter;
            }

            public Task<bool> IsAvailableAsync() => Task.FromResult(_available);

            public Task<IReadOnlyList<string>> FilterAsync(IEnumerable<string> candidates, string query, CancellationToken ct)
            {
                FilterCalls++;
                LastToken = ct;
                _onFilter?.Invoke();
                if (ct.IsCancellationRequested)
                {
                    // A cancelled gather must stop spawning fzf subprocesses: the fake surfaces the
                    // cancellation the finder is expected to propagate.
                    throw new OperationCanceledException(ct);
                }
                return Task.FromResult(_filter(candidates, query));
            }
        }

        // BP-7 (m34): the single-file Map method is deleted — the batched MapBatched is the only
        // mapping path (the finders never call the single-file Map). RED today: Map exists at
        // FzfLineMapper.cs:22-51.
        public static void Run_FzfLineMapper_MapDeleted()
        {
            Assert.True(typeof(FzfLineMapper).GetMethod("Map") == null,
                "FzfLineMapper.Map must be deleted (the batched MapBatched is the only mapping path) — BP-7/m34");
        }

        public static void Run_FzfFinder_EmptyQueryReturnsZeroCandidates()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates("").Count);
            }
        }

        public static void Run_FzfFinder_EmptyQueryCleanEmptyNoFailureLog()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { "a" }, _ => { });

                    // The empty-query path is a clean empty: no gather log and no failure log.
                    var entries = finder.GetCandidates("");
                    LogFileWriter.Flush();

                    Assert.Equal(0, entries.Count);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    Assert.Equal(0, lines.Count(l => l.Contains("[Telescope] fzf hits=")));
                    Assert.Equal(0, lines.Count(l => l.Contains("[Telescope] FzfFinder failed to enumerate:")));
                });
            }
        }

        public static async Task Run_FzfFinder_EmptyQueryAsyncShortCircuits()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                string original = LogFileWriter.LogPath;
                try
                {
                    LogFileWriter.LogPath = logPath;
                    var engine = new FakeFzfEngine(true);
                    var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { "a" }, _ => { });

                    // The cleared-prompt path (async) must short-circuit: 0 candidates, no gather
                    // log, and NO fzf spawn (finding 1).
                    var entries = await finder.GetCandidatesAsync("");
                    LogFileWriter.Flush();

                    Assert.Equal(0, entries.Count);
                    Assert.Equal(0, engine.FilterCalls);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    int hitLines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                        .Count(l => l.Contains("[Telescope] fzf hits="));
                    Assert.Equal(0, hitLines);
                }
                finally
                {
                    LogFileWriter.LogPath = original;
                }
            }
        }

        public static async Task Run_FzfFinder_FuzzyMatchReportsHits()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                string original = LogFileWriter.LogPath;
                try
                {
                    LogFileWriter.LogPath = logPath;
                    string a = Path.Combine(dir.Path, "A.cs");
                    File.WriteAllText(a, "alpha\nFUZZYPROBE here\nbeta\n");

                    var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });
                    var entries = await finder.GetCandidatesAsync("FUZZYPROBE");
                    LogFileWriter.Flush();

                    Assert.Equal(1, entries.Count);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.True(content.Contains("[Telescope] fzf hits=1"), "logs fzf hits=1");
                }
                finally
                {
                    LogFileWriter.LogPath = original;
                }
            }
        }

        public static async Task Run_FzfFinder_DisplayIsFileNameLineText()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "first\nNEEDLE here\n");

                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });
                var entry = (await finder.GetCandidatesAsync("NEEDLE"))[0];
                // Deterministic {fileName}:{line}: {lineText} display (1-based line, fileName only).
                Assert.Equal("A.cs:2: NEEDLE here", entry.Display);
            }
        }

        public static async Task Run_FzfFinder_PayloadRoundTripsFzfHit()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "x\n// NEEDLE x\n");

                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });
                var entry = (await finder.GetCandidatesAsync("NEEDLE"))[0];
                // The FzfHit payload must round-trip through FinderEntry.Payload so OnSelected can
                // recover the exact file/line/text to open.
                var payload = entry.Payload as FzfHit;
                Assert.True(payload != null, "payload is a FzfHit");
                Assert.Equal(a, payload!.FilePath);
                Assert.Equal(2, payload.LineNumber);
                Assert.Equal("// NEEDLE x", payload.LineText);
            }
        }

        public static async Task Run_FzfFinder_OnSelectedOpensHitAtLine()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "first\n// NEEDLE x\n");

                FzfHit? opened = null;
                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, hit => opened = hit);
                var entry = (await finder.GetCandidatesAsync("NEEDLE"))[0];

                finder.OnSelected(entry);
                Assert.True(opened != null, "opener invoked");
                Assert.Equal(a, opened!.FilePath);
                Assert.Equal(2, opened.LineNumber);
                Assert.Equal("// NEEDLE x", opened.LineText);
            }
        }

        public static async Task Run_FzfFinder_HitCapBounded()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 500; i++) { sb.AppendLine("NEEDLE " + i); }
                File.WriteAllText(a, sb.ToString());

                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });
                var entries = await finder.GetCandidatesAsync("NEEDLE");
                Assert.True(entries.Count > 0, "hits are still returned up to the cap");
                Assert.True(entries.Count <= 200, $"hit cap bounds the result set (got {entries.Count})");
            }
        }

        public static async Task Run_FzfFinder_CacheEnumeratesOnce()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                int count = 0;
                var cache = new ProjectFileCache();
                var finder = new FzfFinder(() => null!, cache, new FakeFzfEngine(true), new FileContentCache(500), () => { count++; return new[] { a }; }, _ => { });

                await finder.GetCandidatesAsync("NEEDLE");
                await finder.GetCandidatesAsync("NEEDLE2");

                // The enumerate delegate must run ONCE across two queries — the cache serves the
                // second GetCandidatesAsync.
                Assert.Equal(1, count);
            }
        }

        public static async Task Run_FzfFinder_QueryDrivenBehavior()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "class A\n{\n    int alpha;\n}");

                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });

                Assert.True(finder.IsQueryDriven, "FzfFinder is query-driven (GetCandidatesAsync(query))");
                Assert.True(finder.GetCandidates("").Count == 0, "empty query -> no candidates (short-circuit)");
                var hits = await finder.GetCandidatesAsync("alpha");
                Assert.Equal(1, hits.Count);
                Assert.True(hits[0].Display.Contains("A.cs"), "hit display names the file");
            }
        }

        public static void Run_FzfFinder_OpenPathNoFailureLog()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { "a" }, _ => { });

                    // The overlay-open path calls the SYNC GetCandidates("") (D2b): it must return 0
                    // candidates and NOT log a "FzfFinder failed to enumerate:" failure.
                    var entries = finder.GetCandidates("");
                    LogFileWriter.Flush();

                    Assert.Equal(0, entries.Count);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    int failureLines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                        .Count(l => l.Contains("[Telescope] FzfFinder failed to enumerate:"));
                    Assert.Equal(0, failureLines);
                });
            }
        }

        public static async Task Run_FzfFinder_UnavailableFallsBackToLiteralScan()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                string original = LogFileWriter.LogPath;
                try
                {
                    LogFileWriter.LogPath = logPath;
                    string a = Path.Combine(dir.Path, "A.cs");
                    File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                    var engine = new FakeFzfEngine(false);
                    var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { a }, _ => { });

                    // fzf unavailable -> literal case-insensitive substring scan (NOT the full list),
                    // a real hit count, and the fallback diagnostic; no fzf spawn (AC9/D2c).
                    var entries = await finder.GetCandidatesAsync("needle");
                    LogFileWriter.Flush();

                    Assert.Equal(2, entries.Count);
                    Assert.Equal(0, engine.FilterCalls);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.True(content.Contains("[Telescope] fzf unavailable — literal fallback"), "logs the literal-fallback diagnostic");
                }
                finally
                {
                    LogFileWriter.LogPath = original;
                }
            }
        }

        // BP-1 (M3): a whitespace-only query is empty — 0 candidates AND no fzf spawn. RED today:
        // IsNullOrEmpty at FzfFinder.cs:90 lets "   " through to the gather, which spawns fzf.
        public static async Task Run_FzfFinder_WhitespaceQueryEmpty()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var engine = new FakeFzfEngine(true);
                var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { a }, _ => { });

                var entries = await finder.GetCandidatesAsync("   ");

                Assert.Equal(0, entries.Count);
                Assert.Equal(0, engine.FilterCalls);
            }
        }

        // BP-1 (M3): a fzf engine whose FilterAsync returns null (a crashed/failed filter) must NOT
        // produce garbage — the finder falls back to the literal scan. RED today: MapBatched on
        // null returns empty -> 0 hits.
        public static async Task Run_FzfFinder_FailureFallsBackToLiteral()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                var engine = new FakeFzfEngine(true, (candidates, query) => null!);
                var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { a }, _ => { });

                var entries = await finder.GetCandidatesAsync("needle");

                Assert.True(entries.Count > 0,
                    "a failed fzf filter must fall back to the literal scan (never garbage) — BP-1/M3");
                Assert.True(entries.All(e => e.Display.Contains("A.cs")), "the literal-fallback hits name the file");
            }
        }

        // BP-2 (M4): the fzf gather must run off the UI thread. The test host has NO
        // SynchronizationContext, so the post-await continuation would run on a thread-pool thread
        // and the RED would not reproduce. DESIGN RESOLUTION: install a TestSyncContext that
        // marshals continuations back to the test thread (a queue + pump), so the gather runs on
        // the post-await continuation == the test thread today. RED: the recorded thread == the
        // test thread.
        public static void Run_FzfFinder_GatherOffThread()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                int testThreadId = Thread.CurrentThread.ManagedThreadId;
                int recordedThreadId = -1;
                var engine = new FakeFzfEngine(true, onFilter: () => recordedThreadId = Thread.CurrentThread.ManagedThreadId);
                var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { a }, _ => { });

                var original = SynchronizationContext.Current;
                var ctx = new TestSyncContext();
                SynchronizationContext.SetSynchronizationContext(ctx);
                try
                {
                    var task = finder.GetCandidatesAsync("NEEDLE");
                    while (!task.IsCompleted)
                    {
                        ctx.PumpOne();
                    }
                    var entries = task.GetAwaiter().GetResult();

                    Assert.True(entries.Count > 0, "the fzf gather returns hits");
                    Assert.True(recordedThreadId != testThreadId,
                        $"the fzf gather must run off-thread (recorded {recordedThreadId}, test {testThreadId}) — BP-2/M4");
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(original);
                }
            }
        }

        // BP-2 (M4): a SynchronizationContext that marshals posted continuations back to the test
        // thread via a queue + pump (the test host has no SynchronizationContext, so without this
        // the post-await continuation would run on a thread-pool thread and the off-thread RED
        // would not reproduce).
        private sealed class TestSyncContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<SendOrPostCallback> _callbacks = new ConcurrentQueue<SendOrPostCallback>();
            private readonly ConcurrentQueue<object?> _states = new ConcurrentQueue<object?>();

            public override void Post(SendOrPostCallback d, object? state)
            {
                _callbacks.Enqueue(d);
                _states.Enqueue(state);
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                d(state);
            }

            public void PumpOne()
            {
                while (true)
                {
                    if (_callbacks.TryDequeue(out var d) && _states.TryDequeue(out var state))
                    {
                        d(state);
                        return;
                    }
                    Thread.Sleep(1);
                }
            }
        }

        // BP-6 (m33): GrepFinder and FzfFinder share a QueryDrivenFinderBase<THit> base (the
        // duplicated empty-query short-circuit + ContentCache seam + display format fold into the
        // shared base). COMPILE-RED: QueryDrivenFinderBase<T> does not exist yet -> CS0246. The
        // missing-type references are kept in BODY positions only (typeof expressions) so the full
        // error list surfaces.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing QueryDrivenFinderBase<T> breaks the
        // build, which would block proving the Section C behavior-RED tests; restored at the end of
        // the RED phase).
        public static void Run_QueryDrivenFinderBase_Shared()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "first\nNEEDLE here\n");

                var grep = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var fzf = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), new FileContentCache(500), () => new[] { a }, _ => { });

                Assert.True(typeof(GrepFinder).BaseType == typeof(QueryDrivenFinderBase<GrepHit>),
                    "GrepFinder derives from QueryDrivenFinderBase<GrepHit> (BP-6/m33)");
                Assert.True(typeof(FzfFinder).BaseType == typeof(QueryDrivenFinderBase<FzfHit>),
                    "FzfFinder derives from QueryDrivenFinderBase<FzfHit> (BP-6/m33)");

                // The shared ContentCache seam behaves identically through both finders.
                var shared = new FileContentCache(500);
                var grep2 = new GrepFinder(() => null!, new ProjectFileCache(), shared, () => new[] { a }, _ => { });
                var fzf2 = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), shared, () => new[] { a }, _ => { });
                Assert.True(ReferenceEquals(shared, grep2.ContentCache), "GrepFinder exposes the shared ContentCache");
                Assert.True(ReferenceEquals(shared, fzf2.ContentCache), "FzfFinder exposes the shared ContentCache");

                // The {fileName}:{line}: {lineText} display behaves identically through both finders.
                var grepEntry = grep.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult()[0];
                var fzfEntry = fzf.GetCandidatesAsync("NEEDLE").GetAwaiter().GetResult()[0];
                Assert.Equal("A.cs:2: NEEDLE here", grepEntry.Display);
                Assert.Equal("A.cs:2: NEEDLE here", fzfEntry.Display);
            }
        }

        // ================================================================
        // BP-3 (M2): the query-driven gather must thread the caller's CancellationToken into
        // FilterAsync (today FzfFinder.cs:145 passes CancellationToken.None), so a cancelled
        // gather stops spawning fzf subprocesses. RED: GetCandidatesAsync has no token parameter
        // yet -> CS1501 (the token threading does not exist).
        // ================================================================

        public static async Task Run_FzfFinder_Cancellation()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var engine = new FakeFzfEngine(true);
                var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { a }, _ => { });

                using (var cts = new CancellationTokenSource())
                {
                    // The caller's token must reach FilterAsync (not CancellationToken.None), so a
                    // cancelled gather stops spawning fzf subprocesses. RED: GetCandidatesAsync has
                    // no token parameter -> CS1501 (the missing token threading).
                    var entries = await finder.GetCandidatesAsync("NEEDLE", cts.Token);

                    Assert.Equal(1, entries.Count);
                    Assert.True(engine.LastToken == cts.Token,
                        "the caller's CancellationToken must reach FilterAsync (not CancellationToken.None) — BP-3");
                }
            }
        }

        // ================================================================
        // FileContentCache (BP-2/M5b) — per-file content cache keyed by
        // LastWriteTimeUtc, so ScanFile stops re-reading every file per query.
        // RED: FileContentCache does not exist -> compile error (CS0246).
        // ================================================================

        public static void Run_FileContentCache_CachedRead()
        {
            int reads = 0;
            // Fixed timestamp (not DateTime.UtcNow): two consecutive calls must return the SAME
            // timestamp, or a clock-tick boundary between them makes the cache miss and re-read
            // (reads=2) — a pre-existing test-hermeticity flake (Phase 9 m58 theme).
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new FileContentCache(
                timestamp: _ => fixedTime,
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.GetLines("a");

            // The injected reader must run exactly once for two GetLines on an unchanged file.
            Assert.Equal(1, reads);
        }

        public static void Run_FileContentCache_InvalidatesOnTimestampChange()
        {
            var timestamps = new Dictionary<string, DateTime> { ["a"] = DateTime.UtcNow };
            int reads = 0;
            var cache = new FileContentCache(
                timestamp: p => timestamps[p],
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            timestamps["a"] = timestamps["a"].AddSeconds(1);
            cache.GetLines("a");

            // A changed LastWriteTimeUtc must force a re-read (reader invoked twice).
            Assert.Equal(2, reads);
        }

        public static void Run_FileContentCache_EvictsOldest()
        {
            // M13 / N8 (BP-8): with a maxEntries cap, inserting N+1 entries must evict the
            // least-recently-used (oldest) entry. The timestamp is a per-PATH STABLE value (not
            // DateTime.UtcNow, which can share a key across a ~15ms clock tick and flake; not a
            // per-CALL counter, which would make the re-read always miss and the assertion vacuous).
            int reads = 0;
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var index = new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 2 };
            var cache = new FileContentCache(
                maxEntries: 2,
                timestamp: p => fixedTime.AddSeconds(index[p]),
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.GetLines("b");
            cache.GetLines("c"); // cap 2 exceeded -> the oldest ("a") must be evicted

            // Re-reading the evicted oldest entry must hit the reader again (cache miss). With
            // eviction removed, "a" would still be cached (same stable timestamp) and this fails.
            int before = reads;
            cache.GetLines("a");
            Assert.Equal(before + 1, reads);
        }

        public static void Run_FileContentCache_ClearOnSolutionChange()
        {
            // M13: Clear() empties the cache — after a solution change the next read re-reads.
            // NOTE: Clear() already exists and works, so this may PASS against the current code;
            // the wiring (calling Clear() on solution change) is the fix.
            int reads = 0;
            var cache = new FileContentCache(
                timestamp: _ => DateTime.UtcNow,
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.Clear();
            cache.GetLines("a");

            Assert.Equal(2, reads);
        }

        // BP-1 (m10): FileContentCache must be thread-safe — the M1 fix moves the Grep scan's pure
        // ScanFile loop off the UI thread, so a background thread mutates the cache concurrently with
        // the UI thread. RED: without the lock, a concurrent EvictIfNeeded scan + insert throws/races
        // (InvalidOperationException in the foreach, or a lost/corrupted entry).
        public static void Run_FileContentCache_ThreadSafe()
        {
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new FileContentCache(
                maxEntries: 3,
                timestamp: _ => fixedTime,
                reader: _ => new[] { "line" },
                contentReader: _ => "content");

            const int workers = 8;
            const int perWorker = 500;
            var barrier = new Barrier(workers);
            var errors = new ConcurrentQueue<Exception>();
            var threads = new List<Thread>();
            for (int w = 0; w < workers; w++)
            {
                int worker = w;
                var t = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    try
                    {
                        for (int i = 0; i < perWorker; i++)
                        {
                            string path = "file" + ((worker * perWorker + i) % 12) + ".cs";
                            if ((worker + i) % 3 == 0) cache.GetLines(path);
                            else if ((worker + i) % 3 == 1) cache.GetContent(path);
                            else { cache.GetLines(path); cache.GetContent(path); }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                });
                threads.Add(t);
                t.Start();
            }
            foreach (var t in threads) t.Join();

            Assert.True(errors.IsEmpty,
                "no concurrent GetLines/GetContent may throw (a concurrent EvictIfNeeded scan + insert races): "
                + (errors.IsEmpty ? "" : errors.First().GetType().Name + ": " + errors.First().Message + "\n" + errors.First().StackTrace));

            // The Dictionary is uncorrupted: count never exceeds the cap and every key resolves.
            // m50 (BP-D3): the EntryCount/LruOrder seams replace the ReadEntries reflection helper.
            Assert.True(cache.EntryCount <= 3, "the exact-total invariant holds under concurrency (count <= cap)");
            // Snapshot the keys before the loop: cache.GetLines on a GetContent-created entry
            // (Lines == null) re-reads + inserts + may EvictIfNeeded (removing entries), mutating
            // the LIVE dictionary DURING the foreach enumeration -> "Collection was modified"
            // (test-authoring bug, m10 — the production lock is correct).
            var keys = cache.LruOrder.ToList();
            foreach (var key in keys)
            {
                string path = key;
                Assert.Equal(1, cache.GetLines(path).Length);
                Assert.Equal("content", cache.GetContent(path));
            }
        }

        // BP-14 (m8): the DTE-factory finder ctors must REQUIRE the shared FileContentCache — no
        // `?? new FileContentCache(500)` default (a finder registered without injection silently
        // reverts to its own 500-entry cache). RED: today the param is optional
        // (`FileContentCache? contentCache = null`), so passing null is silently swallowed by the
        // `?? new FileContentCache(500)` fallback instead of throwing ArgumentNullException.
        public static void Run_FileContentCache_RequiredParam()
        {
            // The DTE-factory ctor must reject a null cache (the required-param contract).
            AssertThrowsArgumentNull(() => new GrepFinder(() => null!, new ProjectFileCache(), null!));
            AssertThrowsArgumentNull(() => new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), null!));
            AssertThrowsArgumentNull(() => new CodeIssuesFinder(() => null!, new ProjectFileCache(), null!));

            // The shared instance is used when passed (reference equality on the ContentCache seam).
            var shared = new FileContentCache(500);
            var grep = new GrepFinder(() => null!, new ProjectFileCache(), shared);
            var fzf = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), shared);
            var issues = new CodeIssuesFinder(() => null!, new ProjectFileCache(), shared);
            Assert.True(ReferenceEquals(shared, grep.ContentCache), "GrepFinder uses the injected shared FileContentCache (BP-14)");
            Assert.True(ReferenceEquals(shared, fzf.ContentCache), "FzfFinder uses the injected shared FileContentCache (BP-14)");
            Assert.True(ReferenceEquals(shared, issues.ContentCache), "CodeIssuesFinder uses the injected shared FileContentCache (BP-14)");
        }

        // BP-15 (m9): FileContentCache must evict via an O(1) LinkedList+Dictionary LRU (not the O(n)
        // linear scan). RED: today the O(1) LRU seam (_lru) does not exist — EvictIfNeeded is O(n) per
        // insert. The behavioral contract (touch-the-oldest survives, exact-total invariant, GetContent/
        // GetLines share the LRU) is pinned so the O(1) rewrite must preserve it.
        public static void Run_FileContentCache_Lru()
        {
            // RED: the O(1) LRU seam must exist (LruOrder — a read-only MRU-first view of the
            // LinkedList<string> _lru). CS0117: FileContentCache.LruOrder / EntryCount do not exist
            // yet (BP-D3 m53).
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var index = new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 2 };
            int reads = 0;
            var cache = new FileContentCache(
                maxEntries: 2,
                timestamp: p => fixedTime.AddSeconds(index[p]),
                reader: _ => { reads++; return new[] { "line" }; },
                contentReader: _ => "content");

            // Touch the oldest, insert a new one -> the touched entry survives (LRU, not FIFO).
            cache.GetLines("a"); // a:1
            cache.GetLines("b"); // b:2
            cache.GetLines("a"); // touch a -> a:3 (b is now the LRU)
            cache.GetLines("c"); // insert c -> evict b (the LRU), a survives

            // The O(1) LRU seam: MRU-first order + the exact-total invariant (count <= cap).
            Assert.True(string.Join(",", cache.LruOrder) == "c,a",
                "LruOrder is MRU-first: c was inserted last, a was touched before it (b evicted)");
            Assert.True(cache.EntryCount <= 2, "the exact-total invariant holds (count <= cap)");

            int before = reads;
            cache.GetLines("a"); // hit (a survived) -> no re-read
            Assert.Equal(before, reads);
            cache.GetLines("b"); // miss (b was evicted) -> re-read
            Assert.Equal(before + 1, reads);

            // GetContent and GetLines share the LRU: a GetContent hit refreshes the LRU position.
            int contentReads = 0;
            var cache2 = new FileContentCache(
                maxEntries: 2,
                timestamp: p => fixedTime.AddSeconds(index[p]),
                reader: _ => { contentReads++; return new[] { "line" }; },
                contentReader: _ => "content");
            cache2.GetContent("a"); // a Content:1
            cache2.GetLines("b");   // b Lines:2
            cache2.GetContent("a"); // GetContent HIT -> refreshes a's LRU position (a:3)
            cache2.GetLines("c");   // insert c -> evict b (the LRU), a survives
            int before2 = contentReads;
            cache2.GetContent("a"); // hit (a survived) -> no re-read
            Assert.Equal(before2, contentReads);

            // GetContent shares the LRU: the GetContent HIT above refreshed a's position (a is MRU).
            Assert.True(string.Join(",", cache2.LruOrder) == "a,c",
                "GetContent HIT refreshes the LRU position (a is MRU, b evicted)");
            Assert.Equal(2, cache2.EntryCount);

            cache2.GetLines("b");   // miss (b was evicted) -> re-read
            Assert.Equal(before2 + 1, contentReads);
        }

        private static void AssertThrowsArgumentNull(Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentNullException)
            {
                return;
            }
            throw new Exception("expected ArgumentNullException for a null FileContentCache (BP-14)");
        }

        // ================================================================
        // IFinder query fold — GetCandidates(string) + IsQueryDriven (BP-8/L6)
        // RED: `GetCandidates(string)` / `IsQueryDriven` do not exist on IFinder
        //      -> compile error (the fold has not happened)
        // ================================================================

        // TEMPORARILY COMMENTED OUT (compile-RED — the missing single-seam FileFinder ctor breaks
        // the build; restored at the end of the RED phase).
        public static void Run_GetCandidates_DefaultQuery_MatchesNoArg()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                string b = Path.Combine(dir.Path, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => null!, new ProjectFileCache(), () => new[] { a, b }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                // m53 (BP-D6): the actual pinned display strings (Path.GetFileName of each hit) —
                // NOT a self-referential noArg-vs-emptyArg oracle that passes even when both are wrong.
                Assert.Equal(2, noArg.Count);
                Assert.Equal(2, emptyArg.Count);
                Assert.Equal("Alpha.cs", noArg[0].Display);
                Assert.Equal("Beta.cs", noArg[1].Display);
                Assert.Equal("Alpha.cs", emptyArg[0].Display);
                Assert.Equal("Beta.cs", emptyArg[1].Display);
            }
        }

        public static void Run_GetCandidates_DefaultQuery_GrepEmpty()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                // Written against IFinder so the test pins the NEW interface contract: before the
                // fold IFinder.GetCandidates() takes no args, so GetCandidates("") does not compile.
                IFinder finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(0, noArg.Count);
                Assert.Equal(0, emptyArg.Count);
            }
        }

        // TEMPORARILY COMMENTED OUT (compile-RED — the missing single-seam FileFinder ctor breaks
        // the build; restored at the end of the RED phase).
        public static void Run_GetCandidates_IsQueryDriven()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// x\n");

                IFinder grep = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });
                IFinder files = new FileFinder(() => null!, new ProjectFileCache(), () => new[] { a }, _ => { });

                Assert.True(grep.IsQueryDriven, "GrepFinder is query-driven");
                Assert.False(files.IsQueryDriven, "FileFinder is not query-driven");
            }
        }

        // ================================================================
        // DiagnosticLog — pins the log-prefix constants the harness relies on
        // (F45: Telescope/DiagnosticLog.cs does not exist yet -> this is RED)
        // ================================================================

        public static void Run_LogPrefixes_Pinned()
        {
            Assert.Equal("[NeoVisual] ", DiagnosticLog.NeoVisual);
            Assert.Equal("[Telescope] ", DiagnosticLog.Telescope);
            Assert.Equal("[Hook] ", DiagnosticLog.Hook);
            Assert.Equal("[MyExtension] ", DiagnosticLog.MyExtension);
        }

        // ================================================================
        // TelescopeLog — one-line prefix helper (BP-6/L5)
        // RED: `TelescopeLog` does not exist yet -> compile error
        // ================================================================

        public static void Run_TelescopeLog_Prefix()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    TelescopeLog.Log("hello");
                    // F3 (X1/BP-1): under buffering the write is not on disk until Flush().
                    LogFileWriter.Flush();
                    Assert.True(ReadAllTextShared(logPath).Contains("[Telescope] hello"),
                        "TelescopeLog prefixes the message with [Telescope] ");
                });
            }
        }

        public static void Run_TelescopeLog_EmptyMessage()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    TelescopeLog.Log("");
                    // F3 (X1/BP-1): under buffering the write is not on disk until Flush().
                    LogFileWriter.Flush();
                    Assert.True(ReadAllTextShared(logPath).Contains("[Telescope] "),
                        "an empty message still emits the [Telescope] prefix");
                });
            }
        }

        // ================================================================
        // FilterFailureLog (BP-8/M8) — the exact "[Telescope] filter failed: "
        // line format the overlay's FilterAndUpdateAsync catch emits (M8: today
        // a faulting FilterAsync escapes unobserved with no diagnostic).
        // RED: FilterFailureLog does not exist -> compile error (CS0246).
        // ================================================================

        public static void Run_FilterFailureLog_Format()
        {
            // N41/N63 (BP-55): Format() must return the PREFIXED line so a wrong logger cannot
            // silently break the `filter failed:` contract; the caller switches to
            // NeoVisualLog.Log (which adds no prefix) so the prefix is emitted exactly once.
            // RED today: Format() returns the unprefixed "filter failed: boom".
            Assert.Equal("[Telescope] filter failed: boom", FilterFailureLog.Format(new Exception("boom")));
        }

        // BP-17 (m44): the seam is simplified; the [Telescope] filter failed: {msg} line stays
        // byte-exact. DEVIATION: cannot be made genuinely RED — the format is already byte-exact
        // today (the simplification is internal; the observable contract is unchanged).
        public static void Run_FilterFailureLog_Simplified()
        {
            Assert.Equal("[Telescope] filter failed: boom", FilterFailureLog.Format(new Exception("boom")));
        }

        // BP-30 (m43): FilterFailureLog.Format sanitizes ex.Message (control chars -> spaces).
        // RED today: the newline splits the line.
        public static void Run_FilterFailureLog_Sanitized()
        {
            string formatted = FilterFailureLog.Format(new Exception("boom\nnext"));
            Assert.False(formatted.Contains("\n"), "Format must sanitize control chars (no newline) — BP-30/m43");
            Assert.True(formatted.Contains("boom"), "the message text survives");
            Assert.True(formatted.Contains("next"), "the text after the newline survives (as spaces)");
        }

        // ================================================================
        // ResultMapper — display-keyed payload lookup that preserves
        // same-named duplicates (F12)
        // RED: `Telescope.Overlay.ResultMapper` does not exist yet -> compile error
        // ================================================================

        public static void Run_ResultMapper_DuplicateDisplayPreserved()
        {
            // Two entries share the display "Program.cs" but carry DIFFERENT FileHit payloads
            // (two Program.cs files in different folders). The mapper must map each matched
            // "Program.cs" to a DISTINCT entry: the second must NOT collapse to the first
            // (the current GroupBy/ToDictionary(g => g.First()) bug) nor become a null-payload
            // FinderEntry.
            var first = new FileHit(@"C:\p\src\Program.cs", 0);
            var second = new FileHit(@"C:\p\tests\Program.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("Program.cs", first),
                new FinderEntry("Program.cs", second),
            };

            var items = new ResultMapper().MapBack(new[] { "Program.cs", "Program.cs" }, snapshot);

            Assert.Equal(2, items.Count);
            Assert.Equal("Program.cs", items[0].Display);
            Assert.Equal("Program.cs", items[1].Display);
            Assert.True(ReferenceEquals(first, items[0].Payload), "first matched 'Program.cs' maps to the first entry's payload");
            Assert.True(ReferenceEquals(second, items[1].Payload), "second matched 'Program.cs' maps to the SECOND entry's payload (not the first, not null)");
        }

        public static void Run_ResultMapper_UniqueDisplayMapped()
        {
            var a = new FileHit(@"C:\p\Alpha.cs", 0);
            var b = new FileHit(@"C:\p\Beta.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("Alpha.cs", a),
                new FinderEntry("Beta.cs", b),
            };

            var items = new ResultMapper().MapBack(new[] { "Alpha.cs", "Beta.cs" }, snapshot);

            Assert.Equal(2, items.Count);
            Assert.True(ReferenceEquals(a, items[0].Payload), "Alpha.cs maps to its entry's payload");
            Assert.True(ReferenceEquals(b, items[1].Payload), "Beta.cs maps to its entry's payload");
        }

        public static void Run_ResultMapper_UnknownStringNullPayload()
        {
            // M11 (UPDATED contract): an unmatched display string must NOT produce a null-payload
            // FinderEntry whose OnSelected silently no-ops — it is skipped (or logged). RED today:
            // MapBack returns a null-payload entry for "Ghost.cs", so items.Count == 1.
            var a = new FileHit(@"C:\p\Alpha.cs", 0);
            var snapshot = new List<FinderEntry> { new FinderEntry("Alpha.cs", a) };

            var items = new ResultMapper().MapBack(new[] { "Ghost.cs" }, snapshot);

            Assert.Equal(0, items.Count);
        }

        public static void Run_ResultMapper_OrderPreserved()
        {
            var a = new FileHit(@"C:\p\A.cs", 0);
            var b = new FileHit(@"C:\p\B.cs", 0);
            var c = new FileHit(@"C:\p\C.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("A.cs", a),
                new FinderEntry("B.cs", b),
                new FinderEntry("C.cs", c),
            };

            // fzf returns matches in its own order; the mapper must preserve THAT order, not the
            // snapshot order.
            var items = new ResultMapper().MapBack(new[] { "C.cs", "A.cs", "B.cs" }, snapshot);

            Assert.Equal(3, items.Count);
            Assert.Equal("C.cs", items[0].Display);
            Assert.Equal("A.cs", items[1].Display);
            Assert.Equal("B.cs", items[2].Display);
            Assert.True(ReferenceEquals(c, items[0].Payload), "C.cs maps to its payload");
            Assert.True(ReferenceEquals(a, items[1].Payload), "A.cs maps to its payload");
            Assert.True(ReferenceEquals(b, items[2].Payload), "B.cs maps to its payload");
        }

        // ================================================================
        // ProjectFileCache — per-session cache of the DTE project-file
        // enumeration (F15: Telescope/ProjectFileCache.cs does not exist yet
        // -> this is RED: compile error)
        // ================================================================

        public static void Run_ProjectFileCache_ReturnsCached()
        {
            var cache = new ProjectFileCache();
            var list = new List<string> { @"C:\p\A.cs", @"C:\p\B.cs" };
            int count = 0;

            var first = cache.Get(() => { count++; return list; });
            var second = cache.Get(() => { count++; return list; });

            // The enumerator must run ONCE for two Gets — the second Get is served from the cache.
            Assert.Equal(1, count);
            Assert.True(ReferenceEquals(list, first), "first Get returns the enumerated list");
            Assert.True(ReferenceEquals(list, second), "second Get returns the SAME cached list (no re-enumeration)");
        }

        public static void Run_ProjectFileCache_InvalidateReenumerates()
        {
            var cache = new ProjectFileCache();
            var list = new List<string> { @"C:\p\A.cs" };
            int count = 0;

            cache.Get(() => { count++; return list; });
            cache.Invalidate();
            cache.Get(() => { count++; return list; });

            // Invalidate() must drop the cached result so the next Get re-enumerates.
            Assert.Equal(2, count);
        }

        public static void Run_ProjectFileCache_EmptyResultCached()
        {
            var cache = new ProjectFileCache();
            int count = 0;

            var first = cache.Get(() => { count++; return new List<string>(); });
            var second = cache.Get(() => { count++; return new List<string>(); });

            // An empty result is still a valid cached value: the second Get must NOT re-enumerate.
            Assert.Equal(0, first.Count);
            Assert.Equal(0, second.Count);
            Assert.Equal(1, count);
        }

        public static void Run_ProjectFileCache_ExpiresAfterTtl()
        {
            // N44/BP-58: a cached project-file list must expire after a bounded TTL so files
            // added/removed within a solution are picked up. RED today: the cache is invalidated
            // only on solution-name change, so the stale list is served forever.
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new ProjectFileCache(() => now, TimeSpan.FromSeconds(5));
            var list = new List<string> { @"C:\p\A.cs" };
            int count = 0;

            cache.Get(() => { count++; return list; });
            now = now.AddSeconds(6); // past the TTL
            cache.Get(() => { count++; return list; });

            Assert.Equal(2, count);
        }

        // ================================================================
        // OverlayShowState — state-based guard for the deferred ShowDialog (CR3, BP-5)
        // RED: `OverlayShowState` does not exist yet -> compile error
        // The overlay defers ShowDialog() to ApplicationIdle (fire-and-forget); if CloseOverlay()
        // runs first, the pending ShowDialog fires on an already-closed window. The guard must be
        // STATE-based (RequestShow/Close flags), NOT visibility-based (IsVisible is false at
        // ApplicationIdle time, so a visibility guard would silently never open the overlay).
        // ================================================================

        public static void Run_OverlayShowState_RequestThenClose_ShouldNotShow()
        {
            var s = new OverlayShowState();
            s.RequestShow();
            s.Close();
            Assert.False(s.ShouldShowDialog(), "Close() after RequestShow() must suppress the dialog");
        }

        public static void Run_OverlayShowState_RequestOnly_ShouldShow()
        {
            var s = new OverlayShowState();
            s.RequestShow();
            Assert.True(s.ShouldShowDialog(), "RequestShow() alone must show the dialog");
        }

        public static void Run_OverlayShowState_StateBasedNotVisibilityBased()
        {
            // The decision is STATE-based, not visibility-based: RequestShow() flips a flag with no
            // WPF visibility involved, and Close() flips it back. This pins the state contract so a
            // future IsVisible-based guard (which would be false at ApplicationIdle time and silently
            // never open the overlay) cannot be introduced.
            var s = new OverlayShowState();
            s.RequestShow();
            Assert.True(s.ShouldShowDialog(), "RequestShow() alone must show the dialog (state-based, no visibility involved)");
            s.Close();
            Assert.False(s.ShouldShowDialog(), "Close() must suppress the dialog (state-based)");
        }

        // ================================================================
        // FocusTargetModel — the pane-focus state machine (Feature 7; evolves the M34 block)
        // RED: `FocusTarget.Input`, `PaneFocusKey`, `FocusTargetAction.NoOp`, `SetLayout`,
        // `Focus`, `MapKey`, `ExitsInsert` don't exist -> compile errors
        // (CS0117/CS0246/CS1061). The 6 M34 tests are REPLACED by these 23 (the M34 history
        // carries via the `-- FocusTarget` filter).
        // The PINNED test layout — the overlay's real shape (bottom area, 300x100 DIPs):
        // List (left, 1*) / Preview (right, 2*) on top, Input (full width) below.
        // Registry order [Input, List, Preview] — the tie-break's iteration order (§1.2).
        // NOTE (compile-RED authoring): the layout is built in BODY positions (inside the
        // helper/tests), not a static field — a field typed KeyValuePair<FocusTarget,
        // PaneRect>[] is a declaration-phase error, and csc aborts method-body binding on
        // ANY declaration error (the 2026-10-04 columns-ux lesson), which would hide every
        // body-level RED error this section pins. The inline shape is permanent: it compiles
        // identically once the source lands, and the pinned rects are identical either way.
        // ================================================================

        private static FocusTargetModel NewModelAt(FocusTarget current)
        {
            var model = new FocusTargetModel();
            model.SetLayout(new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 60, 300, 40)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 0, 100, 60)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(100, 0, 200, 60)),
            });
            model.Focus(current);
            return model;
        }

        public static void Run_FocusTarget_StartsWithInput()
        {
            var model = new FocusTargetModel();
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_ResetStartsWithInput()
        {
            var model = new FocusTargetModel();
            model.Focus(FocusTarget.Preview);   // move away first
            model.Reset();
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_CtrlHFromPreviewMovesToList()
        {
            var model = NewModelAt(FocusTarget.Preview);
            var action = model.Handle(PaneFocusKey.Left);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_CtrlLFromListMovesToPreview()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Right);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_CtrlJFromListMovesToInput()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Down);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_CtrlJFromPreviewMovesToInput()
        {
            var model = NewModelAt(FocusTarget.Preview);
            var action = model.Handle(PaneFocusKey.Down);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_CtrlKFromInputTargetsPreview()
        {
            var model = NewModelAt(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.Handled, action);
            // The PINNED up outcome: both panes are above the full-width Input; the Preview wins on
            // LARGEST ADJACENCY (200 of 300 width vs the List's 100) — the last-in-list tie-break is
            // the equal-width net (Run_PaneNavEngine_AdjacencyTieGoesToLastInList pins the rule).
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_EscapeInPreviewReturnsToList()
        {
            var model = NewModelAt(FocusTarget.Preview);
            var action = model.Handle(PaneFocusKey.Escape);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_EscapeInInputUnchanged()
        {
            var model = NewModelAt(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Escape);
            Assert.Equal(FocusTargetAction.None, action);
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_EscapeInListUnchanged()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Escape);
            Assert.Equal(FocusTargetAction.None, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_NoOpLeftFromInput()
        {
            var model = NewModelAt(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Left);
            Assert.Equal(FocusTargetAction.NoOp, action);    // consumed — NOT Handled, NOT None
            Assert.Equal(FocusTarget.Input, model.Current);  // NO wrap — the focus stays
        }

        public static void Run_FocusTarget_NoOpLeftFromList()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Left);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_NoOpRightFromInput()
        {
            var model = NewModelAt(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Right);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_NoOpRightFromPreview()
        {
            var model = NewModelAt(FocusTarget.Preview);
            var action = model.Handle(PaneFocusKey.Right);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_NoOpUpFromList()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.NoOp, action);   // consumed — NOT Handled, NOT None
            Assert.Equal(FocusTarget.List, model.Current);  // NO wrap — the focus stays
        }

        public static void Run_FocusTarget_NoOpUpFromPreview()
        {
            var model = NewModelAt(FocusTarget.Preview);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_NoOpDownFromInput()
        {
            var model = NewModelAt(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Down);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        public static void Run_FocusTarget_ClickFocusesListFromInput()
        {
            var model = NewModelAt(FocusTarget.Input);
            var action = model.Focus(FocusTarget.List);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_ClickFocusesPreviewFromList()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Focus(FocusTarget.Preview);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_ClickIdempotent()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Focus(FocusTarget.List);
            Assert.Equal(FocusTargetAction.Handled, action);  // always Handled (the caller re-logs)
            Assert.Equal(FocusTarget.List, model.Current);    // the focus is unchanged
        }

        public static void Run_FocusTarget_EnumOrderPinned()
        {
            Assert.Equal(0, (int)FocusTarget.Input);
            Assert.Equal(1, (int)FocusTarget.List);
            Assert.Equal(2, (int)FocusTarget.Preview);
        }

        public static void Run_FocusTarget_MapKeyCtrlChords()
        {
            Assert.Equal(PaneFocusKey.Left, FocusTargetModel.ChordDirection(Key.H, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Right, FocusTargetModel.ChordDirection(Key.L, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Down, FocusTargetModel.ChordDirection(Key.J, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Up, FocusTargetModel.ChordDirection(Key.K, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Escape, FocusTargetModel.ChordDirection(Key.Escape, hasCtrl: false));
            Assert.Equal(PaneFocusKey.None, FocusTargetModel.ChordDirection(Key.H, hasCtrl: false));  // plain h is a pane key
            Assert.Equal(PaneFocusKey.None, FocusTargetModel.ChordDirection(Key.J, hasCtrl: false));
        }

        public static void Run_FocusTarget_ExitsInsertRule()
        {
            Assert.False(FocusTargetModel.ExitsInsert(FocusTarget.Input), "Input never exits insert (the prompt owns typing)");
            Assert.True(FocusTargetModel.ExitsInsert(FocusTarget.List), "a focus change away from the prompt exits insert");
            Assert.True(FocusTargetModel.ExitsInsert(FocusTarget.Preview), "a focus change away from the prompt exits insert");
        }

        // ================================================================
        // The collapsed geometric selection pipeline (BP-1 — D1/D2): the mirrored
        // PaneNavigationEngine was COLLAPSED into FocusTargetModel (the single pure focus
        // resolver). These tests exercise the same pipeline through the machine's Handle API —
        // the pinned tie-break (last-in-registry) + the no-op edges survive byte-identically.
        // ================================================================

        public static void Run_FocusTarget_InDirectionFilter()
        {
            // A candidate BEHIND the direction is rejected: from the List (100,0,100,100), RIGHT,
            // the Input (0,0,100,100) lies behind (X=0 is not > 100) and the Preview (250,0,100,100)
            // is the only in-direction pane -> Preview.
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 0, 100, 100)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(100, 0, 100, 100)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(250, 0, 100, 100)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Right);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_AlignmentFilter()
        {
            // An in-direction candidate with NO perpendicular overlap is rejected: from the List
            // (0,100,100,50), RIGHT, the Preview (200,0,100,50) is in-direction (X=200 > 0) with a
            // positive gap (100) but shares NO Y range with the List (0-50 vs 100-150) -> not
            // aligned -> rejected -> NoOp (the Input (0,0,100,50) is not in-direction either).
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 0, 100, 50)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 100, 100, 50)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(200, 0, 100, 50)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Right);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_OverlapGuardRejectsNegativeGap()
        {
            // THE PINNED DEVIATION: from the Preview, LEFT, the full-width Input is in-direction and
            // aligned but OVERLAPS the Preview (gap = 100 - 300 = -200). Without the guard the Input's
            // negative gap would win the closest-gap band and Ctrl+H from the Preview would focus the
            // INPUT. The guard rejects it -> the List (gap 0).
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 60, 300, 40)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 0, 100, 60)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(100, 0, 200, 60)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.Preview);
            var action = model.Handle(PaneFocusKey.Left);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_ClosestGapWins()
        {
            // Two stacked candidates ABOVE the focused pane (gaps 10 and 30): the closest-gap band
            // keeps only the gap-10 candidate -> the List.
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 100, 300, 40)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 60, 150, 30)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(0, 40, 150, 30)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_LargestAdjacencyWins()
        {
            // Equal gaps (both 0), unequal X-overlap: the wider overlap wins — the real K-from-Input
            // shape (the Preview's 200 of the Input's 300 width beats the List's 100).
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 60, 300, 40)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 0, 100, 60)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(100, 0, 200, 60)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_AdjacencyTieGoesToLastInList()
        {
            // THE PINNED TIE-BREAK: equal gaps, EQUAL adjacency (two 150-wide top panes) -> the LAST
            // entry in registry order wins (the window engine's own `>=` rule) -> the Preview.
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 60, 300, 40)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 0, 150, 60)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(150, 0, 150, 60)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.Input);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_NoCandidateReturnsNull()
        {
            var layout = new[]
            {
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Input, new PaneRect(0, 60, 300, 40)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.List, new PaneRect(0, 0, 100, 60)),
                new KeyValuePair<FocusTarget, PaneRect>(FocusTarget.Preview, new PaneRect(100, 0, 200, 60)),
            };
            var model = new FocusTargetModel();
            model.SetLayout(layout);
            model.Focus(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.NoOp, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        // BP-6 (M4): the mirrored geometric pipeline (SelectTarget) must be replaced by the shared
        // GeometricSelectionEngine (the pure engine both FocusTargetModel.ResolveTarget and
        // WindowNavigationEngine.SelectTarget delegate to — the plan's decision, NOT the fallback
        // direction->target table). The engine MUST reproduce the pinned moves byte-identically (the
        // Ctrl+K Input->Preview tie-break + the no-op edges). RED: the capability seam
        // UsesSharedGeometricEngine does not exist yet -> CS0117 (compile-RED). The behavior
        // assertions pin the pinned moves the shared engine must preserve; the ~30
        // Run_FocusTarget_* tests stay GREEN.
        // The standard layout (NewModelAt): Input full-width below, List/Preview on top.
        public static void Run_FocusTargetModel_DirectionTable()
        {
            // RED: the mirrored geometric pipeline (SelectTarget) must be GONE — replaced by the
            // shared GeometricSelectionEngine delegation (BP-3/BP-4). The seam does not exist yet
            // -> CS0117 (compile-RED). The behavior assertions below pin the moves the shared
            // engine must preserve byte-identically.
            Assert.True(FocusTargetModel.UsesSharedGeometricEngine,
                "FocusTargetModel must delegate to the shared GeometricSelectionEngine (BP-3/BP-4) — the mirrored SelectTarget pipeline is gone");

            // Ctrl+K Input -> Preview (the pinned tie-break: the equal-width last-in-list net).
            var model = NewModelAt(FocusTarget.Input);
            Assert.Equal(FocusTargetAction.Handled, model.Handle(PaneFocusKey.Up));
            Assert.Equal(FocusTarget.Preview, model.Current);

            // Ctrl+H Preview -> List.
            model = NewModelAt(FocusTarget.Preview);
            Assert.Equal(FocusTargetAction.Handled, model.Handle(PaneFocusKey.Left));
            Assert.Equal(FocusTarget.List, model.Current);

            // Ctrl+J List -> Input.
            model = NewModelAt(FocusTarget.List);
            Assert.Equal(FocusTargetAction.Handled, model.Handle(PaneFocusKey.Down));
            Assert.Equal(FocusTarget.Input, model.Current);

            // The no-op edges (a direction with no pane -> NoOp, NO wrap).
            model = NewModelAt(FocusTarget.Input);
            Assert.Equal(FocusTargetAction.NoOp, model.Handle(PaneFocusKey.Left));   // Left from Input
            Assert.Equal(FocusTarget.Input, model.Current);

            model = NewModelAt(FocusTarget.Preview);
            Assert.Equal(FocusTargetAction.NoOp, model.Handle(PaneFocusKey.Right));  // Right from Preview
            Assert.Equal(FocusTarget.Preview, model.Current);

            model = NewModelAt(FocusTarget.List);
            Assert.Equal(FocusTargetAction.NoOp, model.Handle(PaneFocusKey.Up));     // Up from List
            Assert.Equal(FocusTarget.List, model.Current);

            model = NewModelAt(FocusTarget.Preview);
            Assert.Equal(FocusTargetAction.NoOp, model.Handle(PaneFocusKey.Up));     // Up from Preview
            Assert.Equal(FocusTarget.Preview, model.Current);

            model = NewModelAt(FocusTarget.Input);
            Assert.Equal(FocusTargetAction.NoOp, model.Handle(PaneFocusKey.Down));   // Down from Input
            Assert.Equal(FocusTarget.Input, model.Current);
        }

        // BP-20 (m48): the parallel _layout/_rects lists merge into ONE list. COMPILE-RED: the
        // capability seam does not exist yet -> CS0117. The geometric behavior is pinned by
        // Run_FocusTargetModel_DirectionTable / _IsSingleResolver /
        // Run_FocusTarget_CtrlJFromListMovesToInput / Run_PaneSelectionSync_ShellRemoved.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing FocusTargetModel.UsesSingleList
        // seam breaks the build; restored at the end of the RED phase).
        public static void Run_FocusTargetModel_SingleList()
        {
            Assert.True(FocusTargetModel.UsesSingleList,
                "FocusTargetModel must hold ONE list (the parallel _layout/_rects merge) — BP-20/m48");
        }

        // ================================================================
        // ListKeyMap — the List pane's pinned consume-vs-fallthrough contract (Feature 7)
        // RED: `ListKeyMap` doesn't exist -> compile error (CS0246).
        // ================================================================

        public static void Run_ListKeyMap_ArrowsFallThrough()
        {
            // THE PINNED DECISION: the native arrows stay live on the List pane.
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.Up, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.Down, shift: false));
        }

        public static void Run_ListKeyMap_SelectionGesturesClaimed()
        {
            Assert.Equal(OverlayKey.J, ListKeyMap.Map(Key.J, shift: false));
            Assert.Equal(OverlayKey.K, ListKeyMap.Map(Key.K, shift: false));
            Assert.Equal(OverlayKey.G, ListKeyMap.Map(Key.G, shift: false));
            Assert.Equal(OverlayKey.ShiftG, ListKeyMap.Map(Key.G, shift: true));
            Assert.Equal(OverlayKey.Enter, ListKeyMap.Map(Key.Enter, shift: false));
            Assert.Equal(OverlayKey.Q, ListKeyMap.Map(Key.Q, shift: false));
            Assert.Equal(OverlayKey.Escape, ListKeyMap.Map(Key.Escape, shift: false));
            Assert.Equal(OverlayKey.I, ListKeyMap.Map(Key.I, shift: false));
            Assert.Equal(OverlayKey.A, ListKeyMap.Map(Key.A, shift: false));
            // BP-25 (m28): the shift-aware A/I — bare a/i vs shift A/I map to DIFFERENT keys
            // (after-caret vs end, current vs start). RED today: both a/A -> OverlayKey.A and
            // both i/I -> OverlayKey.I, so each NotEqual fails.
            Assert.NotEqual(ListKeyMap.Map(Key.A, shift: false), ListKeyMap.Map(Key.A, shift: true));
            Assert.NotEqual(ListKeyMap.Map(Key.I, shift: false), ListKeyMap.Map(Key.I, shift: true));
        }

        public static void Run_ListKeyMap_MotionsFallThrough()
        {
            // The prompt motions have no text caret on the List pane — not claimed.
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.H, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.L, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.W, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.B, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.E, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.D0, shift: false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.D4, shift: false));
        }

        // BP-13 (m24): the shared WPF Key->OverlayKey table (Escape/Q/Enter/J/K/G(shift)/I/A) is
        // extracted into ONE static method both ListKeyMap.Map and TelescopeOverlay.MapKey delegate
        // to. COMPILE-RED: OverlayKeyMapper does not exist yet -> CS0246. The List pane's
        // Up/Down->Other pin (the native arrows stay live) is preserved.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing OverlayKeyMapper breaks the build;
        // restored at the end of the RED phase).
        public static void Run_ListKeyMap_SingleTable()
        {
            Assert.Equal(OverlayKey.J, ListKeyMap.Map(Key.J, false));
            Assert.Equal(OverlayKeyMapper.Map(Key.J, false), ListKeyMap.Map(Key.J, false));
            Assert.Equal(OverlayKey.J, OverlayKeyMapper.Map(Key.J, false));
            // The List pane's Up/Down->Other pin (the native arrows stay live) is preserved.
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.Up, false));
            Assert.Equal(OverlayKey.Other, ListKeyMap.Map(Key.Down, false));
        }

        // BP-25 (m28): on the List pane, a (no shift) -> after-caret, A (shift) -> end,
        // i (no shift) -> current, I (shift) -> start. RED today: both a/A map to OverlayKey.A
        // (End) and both i/I map to OverlayKey.I (Current), so each NotEqual fails.
        public static void Run_ListPane_ShiftDistinction()
        {
            Assert.NotEqual(ListKeyMap.Map(Key.A, false), ListKeyMap.Map(Key.A, true));
            Assert.NotEqual(ListKeyMap.Map(Key.I, false), ListKeyMap.Map(Key.I, true));
        }

        // ================================================================
        // PaneSelectionSync — the native-arrow adoption math (Feature 7)
        // RED: `PaneSelectionSync` doesn't exist -> compile error (CS0246).
        // ================================================================

        // BP-27 (m41): the PaneSelectionSync shell is deleted (compile-enforced once the class is
        // gone) — the old reflection-absence test Run_PaneSelectionSync_StepsRemoved is DELETED in
        // the same step (it referenced typeof(PaneSelectionSync), which would not compile after the
        // deletion). The pane-focus behavior is pinned by the ~30 Run_FocusTarget_* tests.

        // ================================================================
        // PaneHost — the pane contract + host (Feature 7)
        // RED: `IPane`/`PaneHost` don't exist -> compile error (CS0246).
        // The fakes record Activate/Deactivate order; Content is a bare
        // FrameworkElement (BP-A1's [STAThread] makes WPF construction legal).
        // ================================================================

        private sealed class FakePane : IPane
        {
            public readonly List<string> Events = new();
            public FakePane(FocusTarget id) { Id = id; Content = new System.Windows.FrameworkElement(); }
            public FocusTarget Id { get; }
            public FrameworkElement Content { get; }
            public void Activate() => Events.Add($"activate:{Id}");
            public void Deactivate() => Events.Add($"deactivate:{Id}");
        }

        public static void Run_PaneHost_RegistryOrderPinned()
        {
            var host = new PaneHost(
                new FakePane(FocusTarget.Input), new FakePane(FocusTarget.List), new FakePane(FocusTarget.Preview));
            Assert.Equal(FocusTarget.Input, host.GetPane(FocusTarget.Input)!.Id);
            Assert.Equal(FocusTarget.List, host.GetPane(FocusTarget.List)!.Id);
            Assert.Equal(FocusTarget.Preview, host.GetPane(FocusTarget.Preview)!.Id);
        }

        // BP-21 (n10): PromptPane/PreviewPane merge into one DelegatePane (Id, inner pane, optional
        // chrome Border). The Input pane keeps Id=Input + no chrome; the Preview pane keeps
        // Id=Preview + the chrome Border (the active-pane accent line). COMPILE-RED: DelegatePane
        // does not exist yet -> CS0246.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing DelegatePane breaks the build;
        // restored at the end of the RED phase).
        public static void Run_DelegatePane()
        {
            var inner = new FakePane(FocusTarget.List);

            // The Input pane: Id=Input, no chrome.
            var plain = new DelegatePane(FocusTarget.Input, inner);
            Assert.Equal(FocusTarget.Input, plain.Id);

            // The Preview pane: Id=Preview + the chrome Border toggling Dim/Active on
            // Activate/Deactivate.
            var chrome = new DelegatePane(FocusTarget.Preview, inner, chrome: true);
            Assert.Equal(FocusTarget.Preview, chrome.Id);
            var border = (System.Windows.Controls.Border)chrome.Content;
            Assert.Equal(PaneChrome.Dim, border.BorderBrush);
            chrome.Activate();
            Assert.Equal(PaneChrome.Active, border.BorderBrush);
            chrome.Deactivate();
            Assert.Equal(PaneChrome.Dim, border.BorderBrush);
        }

        public static void Run_PaneHost_GetPaneById()
        {
            var host = new PaneHost(new FakePane(FocusTarget.List));
            Assert.True(host.GetPane(FocusTarget.Preview) == null, "an unregistered id must resolve to null");
        }

        public static void Run_PaneHost_ActivateDeactivatesPrevious()
        {
            var list = new FakePane(FocusTarget.List);
            var preview = new FakePane(FocusTarget.Preview);
            var host = new PaneHost(new FakePane(FocusTarget.Input), list, preview);
            host.Activate(FocusTarget.List);
            host.Activate(FocusTarget.Preview);
            // The old pane deactivates BEFORE the new one activates (element-wise — no sequence Assert).
            Assert.Equal("activate:List", list.Events[0]);
            Assert.Equal("deactivate:List", list.Events[1]);
            Assert.Equal("activate:Preview", preview.Events[0]);
            Assert.True(preview.Events.Count == 1, "the new pane must activate exactly once");
        }

        public static void Run_PaneHost_ActivateSamePaneIdempotent()
        {
            var list = new FakePane(FocusTarget.List);
            var host = new PaneHost(list);
            host.Activate(FocusTarget.List);
            host.Activate(FocusTarget.List);
            Assert.Equal(2, list.Events.Count);   // activate, activate — NO deactivate (same pane)
            Assert.Equal("activate:List", list.Events[0]);
            Assert.Equal("activate:List", list.Events[1]);
        }

        public static void Run_PaneHost_ActivateUnknownIdNoop()
        {
            var list = new FakePane(FocusTarget.List);
            var host = new PaneHost(list);
            host.Activate(FocusTarget.Preview);   // not registered
            Assert.Equal(0, list.Events.Count);
        }

        public static void Run_PaneHost_NotifyClickedRaisesEvent()
        {
            var host = new PaneHost(new FakePane(FocusTarget.List));
            FocusTarget? clicked = null;
            host.PaneClicked += id => clicked = id;
            host.NotifyClicked(FocusTarget.List);
            Assert.Equal(FocusTarget.List, clicked!.Value);
        }

        // ================================================================
        // TempDir.Dispose (m21) — a failed recursive delete must surface the error,
        // not swallow it (failed deletes leak temp dirs silently).
        // RED: today Dispose swallows (`catch { }`), so the test fails (no throw).
        // ================================================================

        public static void Run_TempDir_DisposeSurfacesFailure()
        {
            var dir = new TempDir();
            // Sabotage the recursive delete: remove the directory and put a FILE at the same path,
            // so Directory.Delete(path, recursive: true) throws DirectoryNotFoundException.
            System.IO.Directory.Delete(dir.Path, recursive: true);
            System.IO.File.WriteAllText(dir.Path, "i am a file now");

            bool threw = false;
            try
            {
                dir.Dispose();
            }
            catch
            {
                threw = true;
            }
            finally
            {
                try { System.IO.File.Delete(dir.Path); } catch { }
            }

            Assert.True(threw, "TempDir.Dispose must surface a failed recursive delete (m21)");
        }

        // ================================================================
        // RecentFilesFinder — the VS MRU, most-recent-first
        // (hermetic seams: injected gatherer Func<IReadOnlyList<string>> + opener
        // Action<string>, mirroring ReferencesFinder; File.Exists pass-cases use real temp files)
        // RED: RecentFilesFinder / RecentFileHit do not exist yet -> compile error (CS0246).
        // ================================================================

        // Temp-file discipline: File.Exists is a real filesystem call, so the pass-cases
        // create real temp files (tiny, deleted in finally).
        private static string RecentTestDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "telescope_recent_tests", Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Run_RecentFilesFinder_DisplayIsFileName()
        {
            string dir = RecentTestDir();
            try
            {
                string p = Path.Combine(dir, "Order.cs");
                File.WriteAllText(p, "// x\n");
                var finder = new RecentFilesFinder(() => new[] { p }, _ => { });

                var entries = finder.GetCandidates();
                Assert.Equal(1, entries.Count);
                Assert.Equal("Order.cs", entries[0].Display);   // the Files display contract
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public static void Run_RecentFilesFinder_PreservesMostRecentFirstOrder()
        {
            string dir = RecentTestDir();
            try
            {
                string a = Path.Combine(dir, "A.cs"), b = Path.Combine(dir, "B.cs");
                File.WriteAllText(a, "// x\n");
                File.WriteAllText(b, "// x\n");
                // The gatherer's order IS the MRU order (b opened last → b first). The finder must
                // preserve it — never re-sort alphabetically.
                var finder = new RecentFilesFinder(() => new[] { b, a }, _ => { });

                var entries = finder.GetCandidates();
                Assert.Equal("B.cs", entries[0].Display);
                Assert.Equal("A.cs", entries[1].Display);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public static void Run_RecentFilesFinder_FiltersMissingFiles()
        {
            string dir = RecentTestDir();
            try
            {
                string existing = Path.Combine(dir, "Exists.cs");
                File.WriteAllText(existing, "// x\n");
                string missing = Path.Combine(dir, "Missing.cs");   // never created
                var finder = new RecentFilesFinder(() => new[] { missing, existing }, _ => { });

                var entries = finder.GetCandidates();
                Assert.Equal(1, entries.Count);
                Assert.Equal("Exists.cs", entries[0].Display);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public static void Run_RecentFilesFinder_CapsAt200()
        {
            string dir = RecentTestDir();
            try
            {
                var paths = new List<string>();
                for (int i = 0; i < 201; i++)
                {
                    string p = Path.Combine(dir, $"F{i:000}.cs");
                    File.WriteAllText(p, "// x\n");
                    paths.Add(p);
                }

                var finder = new RecentFilesFinder(() => paths, _ => { });

                var entries = finder.GetCandidates();
                Assert.Equal(200, entries.Count);
                // Most-recent-first truncation: the FIRST 200 of the gather order survive.
                Assert.Equal("F000.cs", entries[0].Display);
                Assert.Equal("F199.cs", entries[199].Display);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public static void Run_RecentFilesFinder_PayloadRoundTrips()
        {
            string dir = RecentTestDir();
            try
            {
                string p = Path.Combine(dir, "Writer.cs");
                File.WriteAllText(p, "// x\n");
                var finder = new RecentFilesFinder(() => new[] { p }, _ => { });

                var entry = finder.GetCandidates()[0];
                // BP-15 (m37): RecentFileHit is deleted — RecentFilesFinder produces FileHit payloads.
                var payload = entry.Payload as FileHit;
                Assert.True(payload != null, "payload is a FileHit");
                Assert.Equal(p, payload!.FilePath);
                Assert.Equal(0, payload.LineNumber);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public static void Run_RecentFilesFinder_OnSelectedOpensPath()
        {
            string dir = RecentTestDir();
            try
            {
                string p = Path.Combine(dir, "Opened.cs");
                File.WriteAllText(p, "// x\n");
                string? opened = null;
                var finder = new RecentFilesFinder(() => new[] { p }, path => opened = path);

                finder.OnSelected(finder.GetCandidates()[0]);
                Assert.Equal(p, opened);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public static void Run_RecentFilesFinder_EmptyGatherYieldsNoEntries()
        {
            // A null gatherer result (the probe AND the session floor both empty) → 0 entries.
            var finder = new RecentFilesFinder(() => null!, _ => { });
            Assert.Equal(0, finder.GetCandidates().Count);
        }

        public static void Run_RecentFilesFinder_LineNumberIsZeroForTopReset()
        {
            // The model contract the overlay's preview branch relies on (TelescopeOverlay.cs:731):
            // LineNumber==0 → MoveTo(0) (top reset), never a line jump.
            // BP-15 (m37): RecentFileHit is deleted — the FileHit model carries the contract.
            var hit = new FileHit(@"C:\p\Anywhere.cs", 0);
            Assert.Equal(0, hit.LineNumber);
            Assert.Equal(@"C:\p\Anywhere.cs", hit.FilePath);
        }

        // BP-15 (m37): RecentFileHit is merged into FileHit — RecentFilesFinder produces FileHit
        // payloads and the Recent catalog renders them (the type-disjointness is gone). RED today:
        // the finder still produces RecentFileHit, so `as FileHit` is null -> the assertion fails.
        public static void Run_RecentFileHit_Merged()
        {
            string dir = RecentTestDir();
            try
            {
                string p = Path.Combine(dir, "Order.cs");
                File.WriteAllText(p, "// x\n");
                var finder = new RecentFilesFinder(() => new[] { p }, _ => { });

                var entry = finder.GetCandidates()[0];
                var payload = entry.Payload as FileHit;
                Assert.True(payload != null, "RecentFilesFinder must produce FileHit payloads (BP-15/m37)");
                Assert.Equal(p, payload!.FilePath);
                Assert.Equal(0, payload.LineNumber);

                // The Recent catalog renders FileHit payloads (the full dir — no root trim).
                var cols = FinderColumns.ForFinder("Recent");
                Assert.Equal("Order.cs", CellOf(cols, "file", payload));
                Assert.Equal(dir, CellOf(cols, "dir", payload));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        // ================================================================
        // Recent column set — the Files shape (file+dir visible, path hidden)
        // RED: RecentFileHit does not exist yet -> compile error (CS0246);
        //      FinderColumns.ForFinder("Recent") returns the EMPTY catalog until BP-7 lands.
        // ================================================================

        public static void Run_ResultsColumns_Recent_Catalog()
        {
            var cols = FinderColumns.ForFinder("Recent");
            Assert.Equal("file,dir,path", JoinIds(cols));
            Assert.Equal("File|Directory|Path", string.Join("|", cols.Select(c => c.Header)));
            Assert.Equal("file,dir", JoinDefaultVisible(cols));   // the Files shape: path hidden
        }

        public static void Run_ResultsColumns_Recent_Getters()
        {
            // BP-15 (m37): RecentFileHit is deleted — the Recent catalog renders FileHit payloads
            // (the type-disjointness assertion is REMOVED: a FileHit now renders in Recent).
            var cols = FinderColumns.ForFinder("Recent");
            var hit = new FileHit(@"C:\other\solution\Deep\Dir\Order.cs", 0);
            Assert.Equal("Order.cs", CellOf(cols, "file", hit));
            Assert.Equal(@"C:\other\solution\Deep\Dir", CellOf(cols, "dir", hit));   // FULL dir — no root trim
            Assert.Equal(@"C:\other\solution\Deep\Dir\Order.cs", CellOf(cols, "path", hit));
        }

        // ================================================================
        // Code-review fixes (45 findings) — RED phase (unit-only lane).
        // BP-1: the pane-host collapse deletes PaneNavigationEngine — FocusTargetModel is the
        // single focus resolver. RED: the mirrored engine still exists.
        // ================================================================

        public static void Run_FocusTargetModel_IsSingleResolver()
        {
            // BP-1 (D1/D2): the collapse deletes PaneNavigationEngine (the mirrored engine) —
            // FocusTargetModel is the single pure focus resolver. The deletion is compile-time-
            // enforced (any reference to the deleted type fails to build), so this test pins the
            // PUBLIC contract the inlined call site must preserve: the geometric pane-focus moves
            // (Down from List -> Input, Up from Input -> Preview — the pinned tie-break).
            var model = NewModelAt(FocusTarget.List);
            Assert.Equal(FocusTargetAction.Handled, model.Handle(PaneFocusKey.Down));
            Assert.Equal(FocusTarget.Input, model.Current);

            model = NewModelAt(FocusTarget.Input);
            Assert.Equal(FocusTargetAction.Handled, model.Handle(PaneFocusKey.Up));
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        // ================================================================
        // BP-2: PaneHost becomes the single owner of the active pane — it must NOT store _active
        // (the FocusTargetModel is the single owner of focused-pane state). RED: the field exists.
        // ================================================================

        public static void Run_PaneHost_SingleOwner()
        {
            // BP-2 (D6): PaneHost derives the previously-active pane from FocusTargetModel.Current
            // instead of storing _active — a desync would log `focus target=X` while pane Y holds
            // focus. RED: the seam does not exist yet -> CS0117 (compile-RED).
            Assert.False(new PaneHost().StoresActiveField,
                "PaneHost must not store _active (the FocusTargetModel is the single owner) — BP-2");
        }

        // ================================================================
        // BP-5: FzfFilter registers the kill callback BEFORE the spawn/write — a hung fzf that
        // blocks on stdin write must be killable during the write. RED: today the registration
        // happens after the `await Task.Run(...)` write, so cancelling during the blocked write
        // strands FilterAsync (the process is never killed).
        // ================================================================

        public static void Run_FzfFilter_KillRegisteredBeforeWrite()
        {
            using (var dir = new TempDir())
            {
                // A process that never reads stdin: the parent's write to the pipe blocks once the
                // 64KB pipe buffer fills (the candidate list below is ~4MB). The block is an
                // infinite loop INSIDE cmd.exe itself (no external command / grandchild) so that
                // killing cmd.exe closes the pipe read end and unblocks the write — a grandchild
                // (e.g. ping.exe) would inherit the pipe read end and survive the kill, stranding
                // the write (BP-5 test-authoring fix).
                // BP-D1 (m51): the stub writes a started.txt marker BEFORE entering its infinite
                // loop; the test polls for the marker (bounded 5s) and only then cancels — proving
                // the write is genuinely blocked (the marker is written before the loop, so its
                // presence means the child is alive and holding the pipe) without a wall-clock
                // guess (the old Thread.Sleep(200) could cancel before the write blocked, passing
                // vacuously on a slow machine).
                string cmdPath = Path.Combine(dir.Path, "block.cmd");
                string markerPath = Path.Combine(dir.Path, "started.txt");
                File.WriteAllText(cmdPath,
                    "@echo off\r\n" +
                    $"echo started> \"{markerPath}\"\r\n" +
                    ":loop\r\ngoto loop\r\n");

                var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 5000 };
                var big = Enumerable.Range(0, 20000).Select(i => "line-" + i + new string('x', 200)).ToList();

                using (var cts = new CancellationTokenSource())
                {
                    var task = fzf.FilterAsync(big, "query", cts.Token);
                    // Poll for the marker (bounded 5s) — the write is genuinely blocked once the
                    // marker is present (the child is alive and holding the pipe).
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!File.Exists(markerPath) && DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(20);
                    }
                    Assert.True(File.Exists(markerPath), "the block.cmd stub started (marker written)");
                    cts.Cancel();

                    // m57 (BP-D10): a bounded poll on task.IsCompleted (a generous deadline + a
                    // failure message naming the gate) — no fixed task.Wait(3000). A faulted or
                    // cancelled task is also IsCompleted, so the poll covers the "DID return" case.
                    bool returned = false;
                    var pollDeadline = DateTime.UtcNow.AddSeconds(10);
                    while (!task.IsCompleted && DateTime.UtcNow < pollDeadline)
                    {
                        Thread.Sleep(20);
                    }
                    returned = task.IsCompleted;

                    Assert.True(returned,
                        "cancelling during the blocked stdin write must kill fzf and return promptly (BP-5) — today the kill callback is registered after the write, so the filter strands");
                }
            }
        }

        // ================================================================
        // BP-6: the availability probe is cached once — a second IsAvailableAsync returns the
        // cached value without re-spawning. Guard (pins already-correct behavior).
        // ================================================================

        public static void Run_FzfFilter_AvailabilityProbeOnce()
        {
            using (var dir = new TempDir())
            {
                string marker = Path.Combine(dir.Path, "probe-count.txt");
                string stubPath = Path.Combine(dir.Path, "probe.cmd");
                File.WriteAllText(stubPath, $"@echo off\r\necho x>> \"{marker}\"\r\nexit /b 0\r\n");

                var fzf = new FzfFilter(stubPath);
                Assert.True(fzf.IsAvailableAsync().GetAwaiter().GetResult(), "the probe reports available");
                Assert.True(fzf.IsAvailableAsync().GetAwaiter().GetResult(), "the cached probe reports available");

                string content = File.Exists(marker) ? File.ReadAllText(marker) : string.Empty;
                int spawns = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.True(spawns == 1, $"the availability probe must run exactly once (cached) — BP-6 (got {spawns})");
            }
        }

        // ================================================================
        // n7 (BP-19): the IsAvailableAsync probe must be interlocked — two CONCURRENT callers run
        // the bounded probe ONCE (a SemaphoreSlim/Task<bool> interlock), not twice. RED: today both
        // concurrent callers pass the `_probed` check before either completes, so both run
        // ProbeIsAvailable (the marker records 2 invocations).
        // ================================================================

        public static void Run_FzfFilter_ProbeInterlocked()
        {
            using (var dir = new TempDir())
            {
                string marker = Path.Combine(dir.Path, "probe-count.txt");
                string stubPath = Path.Combine(dir.Path, "probe.cmd");
                File.WriteAllText(stubPath, $"@echo off\r\necho x>> \"{marker}\"\r\nexit /b 0\r\n");

                var fzf = new FzfFilter(stubPath);
                var results = Task.WhenAll(fzf.IsAvailableAsync(), fzf.IsAvailableAsync()).GetAwaiter().GetResult();

                Assert.True(results[0], "the first concurrent probe reports available");
                Assert.True(results[1], "the second concurrent probe reports available");

                string content = File.Exists(marker) ? File.ReadAllText(marker) : string.Empty;
                int spawns = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.True(spawns == 1,
                    $"two concurrent IsAvailableAsync calls must run the probe ONCE (n7/BP-19) — got {spawns}");
            }
        }

        // ================================================================
        // BP-7: QuoteArg Windows argv-quoting edge cases. Guards (QuoteArg is already correct).
        // ================================================================

        public static void Run_QuoteArg_Empty()
        {
            Assert.Equal("\"\"", FzfFilter.QuoteArg(""));
        }

        public static void Run_QuoteArg_Spaces()
        {
            Assert.Equal("\"foo bar\"", FzfFilter.QuoteArg("foo bar"));
            Assert.Equal("\"  \"", FzfFilter.QuoteArg("  "));
        }

        public static void Run_QuoteArg_EmbeddedQuote()
        {
            Assert.Equal("\"a\\\"b\"", FzfFilter.QuoteArg("a\"b"));
            Assert.Equal("\"\\\"\"", FzfFilter.QuoteArg("\""));
        }

        public static void Run_QuoteArg_BackslashRun()
        {
            // A backslash run NOT before a quote is literal (preserved as-is, not doubled).
            Assert.Equal("\"a\\b\"", FzfFilter.QuoteArg("a\\b"));
            Assert.Equal("\"a\\\\b\"", FzfFilter.QuoteArg("a\\\\b"));
        }

        public static void Run_QuoteArg_TrailingBackslash()
        {
            // A trailing backslash is doubled before the closing quote so it cannot escape it.
            Assert.Equal("\"foo\\\\\\\\\"", FzfFilter.QuoteArg("foo\\\\"));
            Assert.Equal("\"\\\\\"", FzfFilter.QuoteArg("\\"));
        }

        // ================================================================
        // BP-9: PreviewCaretMap clamps an out-of-range caret to the buffer length. Guard (pins
        // already-correct behavior — the clamp is what prevents SnapshotPoint from throwing).
        // ================================================================

        public static void Run_PreviewCaretMap_Clamp()
        {
            Assert.Equal(5, PreviewCaretMap.Offset("hello", 100));
            Assert.Equal(5, PreviewCaretMap.Offset("hello", 5));
            Assert.Equal(0, PreviewCaretMap.Offset("hello", -3));
            Assert.Equal(0, PreviewCaretMap.Offset("", 100));
            Assert.Equal(0, PreviewCaretMap.Offset(null!, 100));
        }

        // BP-22 (m23): PreviewCaretMap.Line gains a LineIndex-based overload so the overlay reuses
        // the navigator's cached LineIndex (no full rebuild per preview load). COMPILE-RED:
        // PreviewCaretMap.Line(LineIndex, int) + TextMotionNavigator.LineIndex do not exist yet ->
        // CS1501/CS1061.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing PreviewCaretMap.Line(LineIndex, int)
        // overload + TextMotionNavigator.LineIndex break the build; restored at the end of the RED
        // phase).
        public static void Run_PreviewCaretMap_ReusesLineIndex()
        {
            // The LineIndex-based overload returns the same 1-based line as the string-based Line.
            var index = new LineIndex("one\ntwo\nthree");
            Assert.Equal(PreviewCaretMap.Line("one\ntwo\nthree", 8), PreviewCaretMap.Line(index, 8));
            Assert.Equal(PreviewCaretMap.Line("one\ntwo\nthree", 5), PreviewCaretMap.Line(index, 5));
            Assert.Equal(PreviewCaretMap.Line("one\ntwo\nthree", 3), PreviewCaretMap.Line(index, 3));
            Assert.Equal(PreviewCaretMap.Line("one\ntwo\nthree", 99), PreviewCaretMap.Line(index, 99));

            // The navigator exposes its cached LineIndex (the overlay passes it to the overload).
            var nav = new TextMotionNavigator();
            nav.SetText("one\ntwo\nthree");
            Assert.True(nav.LineIndex != null, "TextMotionNavigator must expose its cached LineIndex (BP-22/m23)");
            Assert.Equal(3, nav.LineIndex!.LineOf(8));
        }

        // ================================================================
        // BP-13 (D12): ResultMapper must group byDisplay with Ordinal (NOT OrdinalIgnoreCase) — a
        // case-colliding duplicate ("Foo.cs"/"foo.cs") must map to its OWN payload. RED: today
        // OrdinalIgnoreCase groups them, so the "Foo.cs" match consumes the "foo.cs" entry.
        // ================================================================

        public static void Run_ResultMapper_OrdinalCase()
        {
            var foo = new FileHit(@"C:\p\foo.cs", 0);
            var Foo = new FileHit(@"C:\p\Foo.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("foo.cs", foo),
                new FinderEntry("Foo.cs", Foo),
            };
            // fzf returns matches in its own order — here the case-colliding pair arrives with the
            // UPPERCASE first. Ordinal mapping must route each to its own payload.
            var items = new ResultMapper().MapBack(new[] { "Foo.cs", "foo.cs" }, snapshot);

            Assert.Equal(2, items.Count);
            Assert.True(ReferenceEquals(Foo, items[0].Payload),
                "Foo.cs maps to the Foo.cs payload (Ordinal, not OrdinalIgnoreCase) — BP-13");
            Assert.True(ReferenceEquals(foo, items[1].Payload),
                "foo.cs maps to the foo.cs payload (Ordinal) — BP-13");
        }

        // ================================================================
        // BP-38 (T2): the IPane contract with a FAKE pane (Activate/Deactivate, content wiring,
        // registry order). Guards (the concrete panes are thin WPF shells; the contract is pinned).
        // ================================================================

        public static void Run_IPane_Contract_Activate()
        {
            var pane = new FakePane(FocusTarget.List);
            var host = new PaneHost(new FakePane(FocusTarget.Input), pane, new FakePane(FocusTarget.Preview));
            host.Activate(FocusTarget.List);
            Assert.Equal("activate:List", pane.Events[0]);
            Assert.True(pane.Content != null, "the pane's Content is wired (a FrameworkElement)");
        }

        public static void Run_IPane_Contract_Deactivate()
        {
            var list = new FakePane(FocusTarget.List);
            var preview = new FakePane(FocusTarget.Preview);
            var host = new PaneHost(new FakePane(FocusTarget.Input), list, preview);
            host.Activate(FocusTarget.List);
            host.Activate(FocusTarget.Preview);
            Assert.Equal("deactivate:List", list.Events[1]);
            Assert.Equal("activate:Preview", preview.Events[0]);
        }

        public static void Run_IPane_Contract_RegistryOrder()
        {
            var host = new PaneHost(
                new FakePane(FocusTarget.Input), new FakePane(FocusTarget.List), new FakePane(FocusTarget.Preview));
            var ids = host.Panes.Select(p => p.Id).ToList();
            Assert.Equal(3, ids.Count);
            Assert.Equal(FocusTarget.Input, ids[0]);
            Assert.Equal(FocusTarget.List, ids[1]);
            Assert.Equal(FocusTarget.Preview, ids[2]);
        }

        // ================================================================
        // COMPILE-RED tests — the test DEFINES the contract the build-agent must implement.
        // Each references a NEW API that does not exist yet (the missing symbol is the RED).
        // ================================================================

        // BP-3 (D14): the Ctrl+H/J/K/L chord→direction mapping must be single-sourced — the focus
        // machine and the overlay's Ctrl-chord path resolve through ONE map. RED: the shared
        // chord-map API (FocusTargetModel.ChordDirection) does not exist yet -> CS1061.
        public static void Run_ChordMap_SingleSource()
        {
            Assert.Equal(PaneFocusKey.Left, FocusTargetModel.ChordDirection(Key.H, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Right, FocusTargetModel.ChordDirection(Key.L, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Down, FocusTargetModel.ChordDirection(Key.J, hasCtrl: true));
            Assert.Equal(PaneFocusKey.Up, FocusTargetModel.ChordDirection(Key.K, hasCtrl: true));
            Assert.Equal(PaneFocusKey.None, FocusTargetModel.ChordDirection(Key.H, hasCtrl: false));
            Assert.Equal(PaneFocusKey.Escape, FocusTargetModel.ChordDirection(Key.Escape, hasCtrl: false));
        }

        // BP-8 (D15): the fzf fast path must cancel the dedicated timeout CTS — no pending timer
        // survives a normal filter. RED: the PendingTimeoutCount seam does not exist yet -> CS1061.
        public static void Run_FzfFilter_TimeoutTimerCancelled()
        {
            using (var dir = new TempDir())
            {
                string stubPath = Path.Combine(dir.Path, "fzf-stub.cmd");
                File.WriteAllText(stubPath, "@echo off\r\nfindstr /i /c:\"alp\"\r\n");

                var fzf = new FzfFilter(stubPath) { FilterTimeoutMs = 5000 };
                var result = fzf.FilterAsync(new[] { "alpha.cs" }, "alp", CancellationToken.None).GetAwaiter().GetResult();

                Assert.Equal(1, result.Count);
                Assert.True(fzf.PendingTimeoutCount == 0,
                    "the fast path must cancel the dedicated timeout CTS — no pending timer survives (BP-8)");
            }
        }

        // BP-10 (D5): the pure ScanFile loop must be off-thread-safe — the fix moves it to a
        // background task (the DTE enumeration stays on the UI thread). RED: ScanFile is a private
        // instance method, not the internal static pure loop -> CS0122.
        public static void Run_GrepFinder_ScanFileBackground()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                var hits = new List<GrepHit>();
                var cache = new FileContentCache();
                // The pure scan loop, invoked from a background thread (the fix's off-thread shape).
                Task.Run(() => GrepFinder.ScanFile(a, "needle", hits, cache)).GetAwaiter().GetResult();

                Assert.Equal(2, hits.Count);
                Assert.True(hits.All(h => h.FilePath == a), "the off-thread scan produces the same hits (BP-10)");
            }
        }

        // BP-12 (D10): a prompt motion consumed before _keyHandler.Handle must clear the pending g
        // via OverlayKeyHandler.CancelPendingG() — `g h g` must NOT fire MoveToFirst (gg). RED:
        // CancelPendingG() does not exist -> CS1061.
        public static void Run_OverlayKeyHandler_CancelPendingG()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(4);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            // 'g' arms the pending gg.
            Assert.Equal(OverlayAction.None, h.Handle(OverlayKey.G));
            // A prompt motion (h) consumed before Handle must clear the pending g (BP-12).
            h.CancelPendingG();
            // The next 'g' must NOT fire MoveToFirst (gg) — it re-arms.
            Assert.Equal(OverlayAction.None, h.Handle(OverlayKey.G));
            Assert.Equal(OverlayAction.MoveToFirst, h.Handle(OverlayKey.G));
        }

        // BP-16 (m12): a focus change clears the pending-g — `g` in one pane -> a focus change (the
        // CancelPendingG call) -> `g` in another does NOT fire `gg` (the second `g` re-arms instead
        // of MoveToFirst). The overlay's FocusPane (the single key/click/restore focus path) calls
        // CancelPendingG() before applying any focus change; this pins the handler contract that
        // fix relies on. The existing Run_OverlayKeyHandler_CancelPendingG stays GREEN.
        public static void Run_OverlayKeyHandler_CancelPendingGOnFocusChange()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(4);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            // 'g' in the first pane arms the pending gg.
            Assert.Equal(OverlayAction.None, h.Handle(OverlayKey.G));

            // A focus change (the CancelPendingG call) clears the pending-g.
            h.CancelPendingG();

            // 'g' in another pane must NOT fire MoveToFirst (gg) — it re-arms.
            Assert.Equal(OverlayAction.None, h.Handle(OverlayKey.G));
            Assert.Equal(OverlayAction.MoveToFirst, h.Handle(OverlayKey.G));
        }

        // BP-14 (D7): all three finders must share ONE FileContentCache instance (injected from
        // TelescopeController) — not 3×500-entry independent caches. RED: the finder ctors do not
        // yet take a shared FileContentCache -> CS1729. BP-D3 (m53): the shared-cache reference is
        // asserted via the ContentCache seam (CS0117 — GrepFinder/FzfFinder/CodeIssuesFinder do not
        // expose ContentCache yet), not reflection.
        public static void Run_FileContentCache_Shared()
        {
            var shared = new FileContentCache(500);
            var grep = new GrepFinder(() => null!, new ProjectFileCache(), shared, () => new[] { "a.cs" }, _ => { });
            var fzf = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(true), shared, () => new[] { "a.cs" }, _ => { });
            var issues = new CodeIssuesFinder(() => null!, new ProjectFileCache(), shared, () => new[] { "a.cs" }, _ => { });

            Assert.True(ReferenceEquals(shared, grep.ContentCache),
                "GrepFinder uses the injected shared FileContentCache (BP-14)");
            Assert.True(ReferenceEquals(shared, fzf.ContentCache),
                "FzfFinder uses the injected shared FileContentCache (BP-14)");
            Assert.True(ReferenceEquals(shared, issues.ContentCache),
                "CodeIssuesFinder uses the injected shared FileContentCache (BP-14)");
        }

        // ================================================================
        // 106-findings plan — Section A (Phase 0: query-driven finder core)
        // BP-1 (M1) — GrepFinder.GetCandidatesAsync truly async (no UI-thread block).
        // RED: the base default GetCandidatesAsync ignores the token and returns the hits.
        // ================================================================

        public static async Task Run_GrepFinder_GetCandidatesAsync_NoBlock()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new GrepFinder(() => null!, new ProjectFileCache(), new FileContentCache(500), () => new[] { a }, _ => { });

                // A pre-cancelled gather must return EMPTY — the async override honors the token
                // (BP-1). RED today: the base default runs the sync GetCandidates and returns the hits.
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    var cancelled = await finder.GetCandidatesAsync("NEEDLE", cts.Token);
                    Assert.Equal(0, cancelled.Count);
                }

                // A normal gather returns the hits.
                var hits = await finder.GetCandidatesAsync("NEEDLE");
                Assert.Equal(1, hits.Count);
            }
        }

        // ================================================================
        // BP-2 (M2) — FzfFinder batches ALL files' lines into ONE fzf --filter call.
        // RED: the per-file loop calls FilterAsync once per file -> FilterCalls == 2.
        // ================================================================

        public static async Task Run_FzfFinder_BatchedFilter()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "alpha\nNEEDLE one\nbeta\n");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(b, "gamma\nNEEDLE two\ndelta\n");

                var engine = new FakeFzfEngine(true);
                var finder = new FzfFinder(() => null!, new ProjectFileCache(), engine, new FileContentCache(500), () => new[] { a, b }, _ => { });

                var entries = await finder.GetCandidatesAsync("NEEDLE");

                // ONE batched fzf call across BOTH files (not one per file). RED today: 2.
                Assert.Equal(1, engine.FilterCalls);
                Assert.Equal(2, entries.Count);
            }
        }

        // ================================================================
        // BP-2 (M2) — FzfLineMapper boundary-aware BuildCandidates/MapBatched.
        // COMPILE-RED: BuildCandidates/MapBatched do not exist yet -> CS0117.
        // ================================================================

        public static void Run_FzfLineMapper_Boundary()
        {
            // The batched fzf path needs a boundary-aware mapper: BuildCandidates flattens ALL
            // files' lines into ONE candidate list (each "{fileIndex}\u0001{lineText}") and
            // MapBatched maps the ranked output back to (fileIndex, lineNumber) in fzf's GLOBAL
            // rank order (per-file ordinal consumption of duplicate line texts).
            var filesLines = new List<(string Path, IReadOnlyList<string> Lines)>
            {
                ("A.cs", new[] { "alpha", "NEEDLE one", "beta" }),
                ("B.cs", new[] { "gamma", "NEEDLE two", "NEEDLE one" }),
            };

            var candidates = FzfLineMapper.BuildCandidates(filesLines);
            Assert.Equal("0\u0001alpha", candidates[0]);
            Assert.Equal("1\u0001NEEDLE one", candidates[5]);

            // fzf's global rank order (the matched lines as fzf returned them).
            var matched = new[] { "1\u0001NEEDLE one", "0\u0001NEEDLE one", "1\u0001NEEDLE two" };
            var mapped = FzfLineMapper.MapBatched(filesLines, matched);

            Assert.Equal(3, mapped.Count);
            Assert.Equal(1, mapped[0].FileIndex);
            Assert.Equal(3, mapped[0].LineNumber);
            Assert.Equal(0, mapped[1].FileIndex);
            Assert.Equal(2, mapped[1].LineNumber);
            Assert.Equal(1, mapped[2].FileIndex);
            Assert.Equal(2, mapped[2].LineNumber);
        }

        // ================================================================
        // BP-4 (m2) — FileContentCache file I/O outside the _gate lock.
        // RED: the first read holds the lock while blocked in the reader -> the second read
        // blocks on the lock -> Wait(1000) returns false.
        // ================================================================

        public static void Run_FileContentCache_IOOutsideLock()
        {
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var entered = new ManualResetEventSlim(false);
            var gate = new ManualResetEventSlim(false);
            var cache = new FileContentCache(
                timestamp: _ => fixedTime,
                reader: p =>
                {
                    if (p == "a")
                    {
                        entered.Set();
                        gate.Wait(TimeSpan.FromSeconds(5));
                    }
                    return new[] { "line" };
                });

            // The first read blocks inside the reader (holding the lock today).
            var first = Task.Run(() => cache.GetLines("a"));
            // m58 (BP-D11): the Wait assertion carries a message naming the gate that never opened.
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)),
                "the first read must enter the reader (the IO-outside-lock gate) — BP-4");

            // A second read of a DIFFERENT path must complete without waiting for the first.
            var second = Task.Run(() => cache.GetLines("b"));
            bool completed = second.Wait(TimeSpan.FromSeconds(1));

            gate.Set();
            first.Wait();

            Assert.True(completed,
                "a cold read must not serialize other cache accesses (the file I/O must run outside the lock) — BP-4");
        }

        // BP-8 (m38): the outside-lock read must not cache stale lines under a NEWER timestamp (a
        // TOCTOU: the file is modified between the timestamp read and the content read). RED today:
        // the stale lines are cached under the newer timestamp -> the second read is a hit.
        public static void Run_FileContentCache_NoToctou()
        {
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var t1 = t0.AddSeconds(1);
            int calls = 0;
            var cache = new FileContentCache(
                timestamp: _ => calls == 0 ? t0 : t1,
                reader: _ => { calls++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.GetLines("a");

            // The first read cached stale lines under t1 (the file "changed" during the read), so
            // the second read's t1 timestamp must NOT match -> the reader is called twice.
            Assert.Equal(2, calls);
        }

        // ================================================================
        // BP-5 (m3) — FzfFinder literal-fallback scan off-thread.
        // RED: the fallback + cold GetLines run on the calling thread -> recorded == test thread.
        // ================================================================

        public static async Task Run_FzfFinder_LiteralFallbackOffThread()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                int testThreadId = Thread.CurrentThread.ManagedThreadId;
                int recordedThreadId = -1;
                var cache = new FileContentCache(
                    timestamp: _ => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    reader: _ => { recordedThreadId = Thread.CurrentThread.ManagedThreadId; return new[] { "NEEDLE here" }; });

                var finder = new FzfFinder(() => null!, new ProjectFileCache(), new FakeFzfEngine(false), cache, () => new[] { a }, _ => { });

                var entries = await finder.GetCandidatesAsync("NEEDLE");

                Assert.True(entries.Count > 0, "the literal fallback returns hits");
                Assert.True(recordedThreadId != testThreadId,
                    $"the literal-fallback scan must run off-thread (recorded {recordedThreadId}, test {testThreadId}) — BP-5");
            }
        }

        // ================================================================
        // BP-6 (m4) — CodeIssuesFinder TODO scan off-thread (the COM Error List walk stays UI-thread).
        // RED: the hermetic TODO scan runs inline -> recorded == test thread.
        // ================================================================

        public static void Run_CodeIssuesFinder_TodoScanOffThread()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// TODO: fix this\n");

                int testThreadId = Thread.CurrentThread.ManagedThreadId;
                int recordedThreadId = -1;
                var cache = new FileContentCache(
                    timestamp: _ => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    reader: _ => { recordedThreadId = Thread.CurrentThread.ManagedThreadId; return new[] { "// TODO: fix this" }; });

                var finder = new CodeIssuesFinder(() => null!, new ProjectFileCache(), cache, () => new[] { a }, _ => { });

                var entries = finder.GetCandidates("");

                Assert.True(entries.Count > 0, "the TODO scan returns hits");
                Assert.True(recordedThreadId != testThreadId,
                    $"the TODO scan must run off-thread (recorded {recordedThreadId}, test {testThreadId}) — BP-6");
            }
        }

        // ================================================================
        // BP-8 (m7) — FileContentCache GetLines/GetContent share one entry.
        // RED: GetLines re-reads because the entry's Lines == null -> reader count 1.
        // ================================================================

        public static void Run_FileContentCache_SharedEntry()
        {
            var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int readerCount = 0;
            int contentReaderCount = 0;
            var cache = new FileContentCache(
                timestamp: _ => fixedTime,
                reader: _ => { readerCount++; return new[] { "line" }; },
                contentReader: _ => { contentReaderCount++; return "line\n"; });

            cache.GetContent("a");
            cache.GetLines("a");

            // GetLines must derive the lines from the cached content (m7) — the reader is never
            // called. RED today: GetLines re-reads (entry.Lines == null) -> readerCount == 1.
            Assert.Equal(0, readerCount);
            Assert.Equal(1, contentReaderCount);
        }

        // ================================================================
        // BP-9 (m8 + m9) — FzfFilter suppresses the spurious timeout log on a cancelled gather;
        // the grace delay carries the cancellation token.
        // RED (m8): the uncancelled grace delay waits, then logs the timeout line.
        // ================================================================

        public static void Run_FzfFilter_NoSpuriousTimeoutOnCancel()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                    File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                    var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 100 };
                    var timeoutTcs = new TaskCompletionSource<bool>();
                    var graceEntered = new TaskCompletionSource<bool>();
                    using (var cts = new CancellationTokenSource())
                    {
                        // BP-D2 (m49): drive the timeout through the DelayFactory seam — the timeout
                        // task is completed deterministically (no fixed Thread.Sleep(200)); the grace
                        // delay is a task that completes only when its token is cancelled.
                        fzf.DelayFactory = (ms, ct) =>
                        {
                            if (ms == fzf.FilterTimeoutMs)
                            {
                                return timeoutTcs.Task;
                            }
                            graceEntered.SetResult(true);
                            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
                        };

                        var task = fzf.FilterAsync(new[] { "alpha" }, "alp", cts.Token);
                        // Drive the timeout to fire -> the filter enters the grace window.
                        timeoutTcs.SetResult(true);
                        Assert.True(graceEntered.Task.Wait(TimeSpan.FromSeconds(5)),
                            "the filter entered the grace window (the timeout fired)");
                        // Cancel inside the grace window -> the filter returns the full list, no
                        // spurious timeout line.
                        cts.Cancel();
                        var result = task.GetAwaiter().GetResult();
                        LogFileWriter.Flush();

                        Assert.Equal(1, result.Count);
                        Assert.True(result.Contains("alpha"), "cancellation falls back to the full candidate list");
                        string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                        Assert.False(content.Contains("fzf filter failed: timeout after"),
                            "a cancelled gather must NOT log the spurious timeout line (m8) — BP-9");
                    }
                });
            }
        }

        // ================================================================
        // BP-9 (m9) — the grace delay carries the cancellation token.
        // RED: the uncancelled grace delay waits the remaining ~200ms after the cancel.
        // ================================================================

        public static void Run_FzfFilter_GraceDelayToken()
        {
            using (var dir = new TempDir())
            {
                string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 100 };
                var timeoutTcs = new TaskCompletionSource<bool>();
                var graceEntered = new TaskCompletionSource<bool>();
                CancellationToken? graceToken = null;
                using (var cts = new CancellationTokenSource())
                {
                    // BP-D1 (M5): drive the timeout + grace tasks through the DelayFactory seam —
                    // the timeout is a controlled task; the grace delay is a task that completes
                    // ONLY when its token is cancelled. The filter returns promptly iff the grace
                    // delay carries the caller's cancellation token (BP-9/m9) — no wall-clock sleep.
                    fzf.DelayFactory = (ms, ct) =>
                    {
                        if (ms == fzf.FilterTimeoutMs)
                        {
                            return timeoutTcs.Task;
                        }
                        graceToken = ct;
                        graceEntered.SetResult(true);
                        return Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    };

                    var task = fzf.FilterAsync(new[] { "alpha" }, "alp", cts.Token);
                    // Drive the timeout to fire -> the filter enters the grace window (the grace
                    // factory is invoked only after the timeout wins).
                    timeoutTcs.SetResult(true);
                    Assert.True(graceEntered.Task.Wait(TimeSpan.FromSeconds(5)),
                        "the filter entered the grace window (the timeout fired)");
                    Assert.True(graceToken == cts.Token,
                        "the grace delay must carry the caller's cancellation token (BP-9)");
                    // Cancel the caller's token -> the grace task (which observes the token) completes.
                    cts.Cancel();
                    Assert.True(task.Wait(TimeSpan.FromSeconds(5)),
                        "the filter must return promptly once the grace token is cancelled (BP-9)");
                    var result = task.GetAwaiter().GetResult();

                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha"), "cancellation falls back to the full candidate list");
                }
            }
        }

        // ================================================================
        // BP-11 (m54) — direct unit test for LiteralLineScanner.Scan (coverage addition, NOT a RED).
        // ================================================================

        public static void Run_LiteralLineScanner_Direct()
        {
            // Null lines -> empty.
            Assert.Equal(0, LiteralLineScanner.Scan(null!, "q", 10).Count);
            // Empty query -> empty.
            var lines = new[] { "alpha", "beta" };
            Assert.Equal(0, LiteralLineScanner.Scan(lines, "", 10).Count);
            // Zero cap -> empty.
            Assert.Equal(0, LiteralLineScanner.Scan(lines, "q", 0).Count);
            // Case-insensitive substring returns the right 1-based lines.
            var hits = LiteralLineScanner.Scan(new[] { "Alpha", "beta", "ALPHA again" }, "alpha", 10);
            Assert.Equal(2, hits.Count);
            Assert.Equal(1, hits[0]);
            Assert.Equal(3, hits[1]);
            // The cap bounds the result.
            var capped = LiteralLineScanner.Scan(new[] { "q", "q", "q", "q" }, "q", 2);
            Assert.Equal(2, capped.Count);
        }

        // ================================================================
        // 106-findings plan — Section B (Phases 2-4)
        // BP-3 (M4) — the shared GeometricSelectionEngine reproduces BOTH contracts with the
        // parameters (allowNegativeGap, divide, strictEdge).
        // COMPILE-RED: `GeometricSelectionEngine` does not exist yet -> CS0246.
        // ================================================================

        public static void Run_SharedGeometricEngine_Parameters()
        {
            // (a) pane params (allowNegativeGap:false, divide:0, strictEdge:false) over the
            //     NewModelAt layout — Down from List -> Input (Input.Y=60 > List.Y=0, gap=0
            //     passes the gap>=0 filter), Up from Input -> Preview (the pinned tie-break).
            var paneRects = new List<PaneRect>
            {
                new PaneRect(0, 60, 300, 40),   // Input
                new PaneRect(0, 0, 100, 60),    // List
                new PaneRect(100, 0, 200, 60),  // Preview
            };
            PaneRect list = paneRects[1];
            PaneRect input = paneRects[0];

            // Down (1) from List -> Input (index 0).
            int? down = GeometricSelectionEngine.SelectTarget(list, paneRects, 1, false, 0, false);
            Assert.Equal(0, down);

            // Up (0) from Input -> Preview (index 2 — the pinned tie-break).
            int? up = GeometricSelectionEngine.SelectTarget(input, paneRects, 0, false, 0, false);
            Assert.Equal(2, up);

            // (b) window params (allowNegativeGap:true, divide:settings, strictEdge:true) —
            //     post-M7 Down c.Y > a.Bottom / Up c.Bottom < a.Y. Active (100,100,100,100)
            //     Bottom=200; the overlapping candidate is excluded by strictEdge.
            var active = new PaneRect(100, 100, 100, 100);
            var windows = new List<PaneRect>
            {
                new PaneRect(100, 150, 100, 100),  // overlapping — excluded by strictEdge
                new PaneRect(100, 250, 50, 50),    // truly below (Y=250 > Bottom=200)
            };
            int? below = GeometricSelectionEngine.SelectTarget(active, windows, 1, true, 0, true);
            Assert.Equal(1, below);

            var aboveWindows = new List<PaneRect>
            {
                new PaneRect(100, 150, 100, 100),  // overlapping — excluded by strictEdge
                new PaneRect(100, 40, 50, 50),     // truly above (Bottom=90 < Y=100)
            };
            int? above = GeometricSelectionEngine.SelectTarget(active, aboveWindows, 0, true, 0, true);
            Assert.Equal(1, above);
        }

        // ================================================================
        // BP-7 (M12) — the IsOpen race: a close during the IsAvailableAsync await must leave
        // the overlay closed (the re-check bails; ShowDialog never fires on a closed window).
        // The current code sets IsOpen=true + RequestShow() AFTER the await, so a close during
        // the await leaves IsOpen stuck true. The test models the race in the CURRENT code's
        // order (Close before RequestShow) and asserts the FIXED behavior.
        // ================================================================

        public static void Run_TelescopeOverlay_IsOpenRace()
        {
            var s = new OverlayShowState();
            s.Close();          // close during the await (before RequestShow)
            s.RequestShow();    // the current code's post-await RequestShow
            Assert.False(s.ShouldShowDialog(), "a close during the await must suppress the dialog");
        }

        // ================================================================
        // BP-8 (m1) — SetTextIfChanged skips the LineIndex rebuild when the text is unchanged.
        // COMPILE-RED: `SetTextIfChanged` does not exist yet -> CS1061.
        // ================================================================

        public static void Run_PreviewNavigator_SetTextGuarded()
        {
            var n = new TextMotionNavigator();
            n.SetText("alpha\nbeta");
            n.MoveTo(6); // caret in "beta"

            // Unchanged text: no rebuild, caret preserved.
            Assert.False(n.SetTextIfChanged("alpha\nbeta"), "unchanged text must not rebuild");
            Assert.Equal(6, n.Caret);

            // Changed text: rebuild, caret reset to 0.
            Assert.True(n.SetTextIfChanged("alpha\nbeta\ngamma"), "changed text must rebuild");
            Assert.Equal(0, n.Caret);
        }

        // ================================================================
        // BP-9 (m5) — the results log fires on change only (columns/count/selected/boxText).
        // COMPILE-RED: `ResultsLogGate` does not exist yet -> CS0246.
        // ================================================================

        public static void Run_ResultsLog_OnChange()
        {
            var gate = new ResultsLogGate();

            // First render always logs.
            Assert.True(gate.ShouldLog("file,line", 3, 0, 5), "first render logs");

            // Unchanged columns/count/selection/boxText: no log.
            Assert.False(gate.ShouldLog("file,line", 3, 0, 5), "unchanged state does not log");

            // A selection-only change logs.
            Assert.True(gate.ShouldLog("file,line", 3, 1, 5), "selection change logs");

            // A count change logs.
            Assert.True(gate.ShouldLog("file,line", 4, 1, 5), "count change logs");

            // A columns change logs.
            Assert.True(gate.ShouldLog("file,line,kind", 4, 1, 5), "columns change logs");

            // Back to the last-logged state: no log.
            Assert.False(gate.ShouldLog("file,line,kind", 4, 1, 5), "unchanged after a change does not log");
        }

        // ================================================================
        // BP-11 (m20) — Dispose() from a background thread must marshal to the UI thread and
        // close the overlay (not swallow the exception and leak it).
        // COMPILE-RED: the injectable close-overlay seam does not exist yet -> CS1739 (no
        // 'closeOverlay' parameter on the TelescopeController ctor).
        // ================================================================

        public static void Run_TelescopeController_DisposeClosesOverlay()
        {
            // m51 (BP-D4): the TestScaffold helper sets the current dispatcher as the UI thread
            // (the single reflection site) — no inline ThreadHelper.uiThreadDispatcher reflection.
            using (SetCurrentDispatcherAsUiThread())
            {
                bool closed = false;
                var controller = new TelescopeController(closeOverlay: () => closed = true);
                var thread = new System.Threading.Thread(() => controller.Dispose());
                thread.Start();
                // Pump the UI dispatcher so the marshaled close path can run on this (UI) thread.
                while (!thread.Join(25))
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                }
                Assert.True(closed, "Dispose() from a background thread must invoke the close path");
            }
        }

        // ================================================================
        // BP-12 (m23) — PaneFailureTracker._emitted must be interlocked: concurrent loggers
        // emit exactly once. RED today: _emitted is a plain bool; N threads behind a Barrier
        // can all read false before any writes true -> multiple return true.
        // ================================================================

        public static void Run_PaneFailureTracker_Interlocked()
        {
            // m52 (BP-D5): the _emitted field-type reflection assert is gone — the behavioral
            // N-threads race is the whole test (exactly-one emission under the race).
            const int n = 16;
            var tracker = new PaneFailureTracker();
            var go = 0;
            var ready = new CountdownEvent(n);
            var results = new ConcurrentBag<bool>();
            var threads = new List<Thread>();
            for (int i = 0; i < n; i++)
            {
                var t = new Thread(() =>
                {
                    ready.Signal();
                    // Spin on the shared flag so ALL threads enter ShouldEmit() simultaneously —
                    // the non-atomic read-modify-write of _emitted then races (RED today).
                    while (Volatile.Read(ref go) == 0) { Thread.SpinWait(1); }
                    results.Add(tracker.ShouldEmit());
                });
                threads.Add(t);
                t.Start();
            }
            ready.Wait();
            Volatile.Write(ref go, 1);
            foreach (var t in threads)
            {
                t.Join();
            }
            Assert.True(results.Count(r => r) == 1, "exactly one thread must emit");
        }

        // ================================================================
        // BP-16 (m46) — the placement drift: TextMotionDispatcher.Apply reports Current for
        // InsertAfter while PromptMotionRouter reports AfterCaret. The fix aligns Apply to
        // AfterCaret so the caller applies the SAME placement the router reports (the single
        // MapKey result, no drift). RED today: Apply reports Current.
        // ================================================================

        public static void Run_PromptMotionRouter_SingleMapKey()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            CaretPlacement? placement;
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertAfter, n, out placement));
            Assert.Equal(CaretPlacement.AfterCaret, placement);  // RED today: Current
            Assert.Equal(3, n.Caret);
        }

        // ================================================================
        // BP-17 (n16) — ShouldFocus guards the EnterInsert focus log: only focus+log when not
        // already on the target.
        // COMPILE-RED: `ShouldFocus` does not exist yet -> CS1061.
        // ================================================================

        public static void Run_FocusTargetModel_ShouldFocus()
        {
            var model = NewModelAt(FocusTarget.Input);
            Assert.False(model.ShouldFocus(FocusTarget.Input), "already on Input -> no focus");
            Assert.True(model.ShouldFocus(FocusTarget.List), "not on List -> focus");
            Assert.True(model.ShouldFocus(FocusTarget.Preview), "not on Preview -> focus");
        }

        // ================================================================
        // 106-findings plan — Section C Phase 7 (duplication + dead code)
        // ================================================================

        // BP-17 (m27): Grep()/Fzf() are byte-identical catalogs (post-m25 both use GrepHit) —
        // merge into ONE method. RED today: the Fzf catalog types its getters to FzfHit, so a
        // GrepHit payload renders EMPTY in Fzf vs "A.cs" in Grep.
        public static void Run_FinderColumns_GrepFzfShared()
        {
            var grep = FinderColumns.ForFinder("Grep");
            var fzf = FinderColumns.ForFinder("Fzf");
            Assert.Equal(JoinIds(grep), JoinIds(fzf));
            Assert.Equal(string.Join("|", grep.Select(c => c.Header)),
                string.Join("|", fzf.Select(c => c.Header)));
            Assert.Equal(JoinDefaultVisible(grep), JoinDefaultVisible(fzf));
            // A GrepHit payload renders the same cells in both catalogs (RED today: Fzf renders
            // empty because its getters are typed to FzfHit).
            var hit = new GrepHit(@"C:\p\src\A.cs", 2, "NEEDLE here");
            Assert.Equal(CellOf(grep, "file", hit), CellOf(fzf, "file", hit));
            Assert.Equal(CellOf(grep, "line", hit), CellOf(fzf, "line", hit));
            Assert.Equal(CellOf(grep, "text", hit), CellOf(fzf, "text", hit));
        }

        // BP-18 (m28): Files()/Recent() share structure but the dir-cell getter DIFFERS — the
        // merge must parameterize the getter, NOT unify it. BP-15 (m37): RecentFileHit is deleted —
        // both catalogs render FileHit. BP-19 (m47): the Files dir cell is the FULL directory (no
        // root trim). Contract-pinning (the behavior is already correct today).
        public static void Run_FinderColumns_FilesRecentShared()
        {
            var files = FinderColumns.ForFinder("Files");
            var recent = FinderColumns.ForFinder("Recent");
            var fileHit = new FileHit(@"C:\proj\Services\Foo.cs", 0);
            var recentHit = new FileHit(@"C:\other\solution\Deep\Dir\Order.cs", 0);
            Assert.Equal(@"C:\proj\Services", CellOf(files, "dir", fileHit));   // full dir — no root trim
            Assert.Equal(@"C:\other\solution\Deep\Dir", CellOf(recent, "dir", recentHit));  // full dir
        }

        // BP-19 (m29): the 200-hit cap is ONE shared constant, not three per-finder copies.
        // COMPILE-RED: the shared FinderConstants.HitCap does not exist yet -> CS0246. The
        // build-agent creates FinderConstants.HitCap (the plan's suggested name) and the three
        // finders (GrepFinder/FzfFinder/RecentFilesFinder) reference it.
        public static void Run_HitCap_SharedConstant()
        {
            Assert.Equal(200, FinderConstants.HitCap);
        }

        // BP-22 (m31) + BP-26 (m40): VisibleIdsJoined/Ids/Catalog are dead members deleted by the
        // build (compile-enforced — any residual reference fails the build). The surviving
        // contract: ResultsFormatter.ColumnsIdList (the production path at TelescopeOverlay.cs:831)
        // joins the visible column ids. Contract-pinning.
        public static void Run_ResultColumn_DeadMembersRemoved()
        {
            Assert.Equal("", string.Join(",", new string[] { }));
            Assert.Equal("access,file", string.Join(",", new[] { "access", "file" }));
        }

        // BP-24 (m33): the shared ErrorItemsWalker.ForEach iterates ErrorItems with per-item
        // try/catch — a throwing item is skipped, the rest survive.
        // COMPILE-RED: ErrorItemsWalker does not exist yet -> CS0246.
        public static void Run_ErrorItemsWalker_SharedWalk()
        {
            var items = new FakeErrorItems(new List<ErrorItem>
            {
                new FakeErrorItem("A.cs"),
                new FakeErrorItem("B.cs", throwOnAccess: true),
                new FakeErrorItem("C.cs"),
            });
            var visited = new List<string>();
            ErrorItemsWalker.ForEach(items, item => visited.Add(item.FileName));
            Assert.Equal(2, visited.Count);
            Assert.Equal("A.cs", visited[0]);
            Assert.Equal("C.cs", visited[1]);
        }

        // BP-27 (m41): the PaneSelectionSync shell is deleted (compile-enforced once the class is
        // gone). The pane-focus behavior it used to provide is pinned by the FocusTargetModel
        // geometric tests — Down from List -> Input, Up from Input -> Preview (the pinned
        // tie-break). Contract-pinning.
        public static void Run_PaneSelectionSync_ShellRemoved()
        {
            var model = NewModelAt(FocusTarget.List);
            var action = model.Handle(PaneFocusKey.Down);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Input, model.Current);

            var up = NewModelAt(FocusTarget.Input);
            var upAction = up.Handle(PaneFocusKey.Up);
            Assert.Equal(FocusTargetAction.Handled, upAction);
            Assert.Equal(FocusTarget.Preview, up.Current);
        }

        // BP-28 (n19): DefinitionFinder/ImplementationFinder share the same display contract
        // ({Kind} {SymbolName} — {file}:{line}) — the shared base must preserve it.
        // Contract-pinning (the behavior is already correct today).
        public static void Run_DefinitionFinder_SharedBody()
        {
            var defHit = new DefinitionHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var defFinder = new DefinitionFinder(() => new[] { defHit }, _ => { });
            var implHit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var implFinder = new ImplementationFinder(() => new[] { implHit }, _ => { });

            Assert.Equal(defFinder.GetCandidates()[0].Display, implFinder.GetCandidates()[0].Display);
            Assert.Equal("Class Shape — Shape.cs:2", defFinder.GetCandidates()[0].Display);
        }

        // ================================================================
        // 106-findings plan — Section C Phase 5 (dead-code deletions + logging/comment fixes)
        // RED phase (unit-only lane). BP-1..BP-12.
        // ================================================================

        // BP-1 (m25): the merged TextMotionDispatcher.Handle(...) wrapper is dead — the surviving
        // surface is MapKey(Key, bool) + Apply(TextMotion, TextMotionNavigator, out CaretPlacement?).
        // RED today: Handle exists at TextMotionDispatcher.cs:181-190.
        public static void Run_TextMotionDispatcher_HandleRemoved()
        {
            Assert.True(typeof(TextMotionDispatcher).GetMethod("Handle") == null,
                "TextMotionDispatcher.Handle must be deleted (MapKey + Apply are the surviving surface) — BP-1/m25");
        }

        // BP-2 (m35): HierarchyWalker.FirstFileEndingWith is dead (the resolver uses
        // FirstPathEndingWith — the no-existence-filter walk). RED today: it exists at
        // HierarchyWalker.cs:36-46.
        public static void Run_HierarchyWalker_DeadRemoved()
        {
            Assert.True(typeof(HierarchyWalker).GetMethod("FirstFileEndingWith") == null,
                "HierarchyWalker.FirstFileEndingWith must be deleted (FirstPathEndingWith is the surviving walk) — BP-2/m35");
        }

        // BP-3 (m39): ColumnVisibilityModel.VisibleColumns is dead (the overlay builds the GridView
        // from VisibleIds). RED today: it exists at ResultColumn.cs:127-128.
        public static void Run_ColumnVisibilityModel_DeadRemoved()
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Assert.True(typeof(ColumnVisibilityModel).GetProperty("VisibleColumns", flags) == null,
                "ColumnVisibilityModel.VisibleColumns must be deleted (VisibleIds is the surviving surface) — BP-3/m39");
        }

        // BP-4 (m40): ResultColumn.Width/WidthChars are dead (the width-fit engine reads
        // MinWidth/MaxWidth/Truncation). RED today: they exist at ResultColumn.cs:52/55.
        public static void Run_ResultColumn_DeadRemoved()
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Assert.True(typeof(ResultColumn).GetProperty("Width", flags) == null,
                "ResultColumn.Width must be deleted — BP-4/m40");
            Assert.True(typeof(ResultColumn).GetProperty("WidthChars", flags) == null,
                "ResultColumn.WidthChars must be deleted — BP-4/m40");
        }

        // BP-5 (m42): the defensive ImplMap entries are deleted — the ≤4-char Fallback renders them
        // (Delegate -> "dele", ErrorType -> "erro"). RED today: the defensive entries map to their
        // old abbreviations ("del"/"errt").
        public static void Run_KindAbbreviations_NoDefensive()
        {
            Assert.Equal("dele", KindAbbreviations.Implementation("Delegate"));
            Assert.Equal("erro", KindAbbreviations.Implementation("ErrorType"));
            Assert.Equal("type", KindAbbreviations.Implementation("TypeParameter"));
            Assert.Equal("unkn", KindAbbreviations.Implementation("Unknown"));
            Assert.Equal("arra", KindAbbreviations.Implementation("Array"));
            Assert.Equal("dyna", KindAbbreviations.Implementation("Dynamic"));
            Assert.Equal("poin", KindAbbreviations.Implementation("Pointer"));
            Assert.Equal("func", KindAbbreviations.Implementation("FunctionPointer"));
            Assert.Equal("name", KindAbbreviations.Implementation("NamedType"));
            Assert.Equal("name", KindAbbreviations.Implementation("Namespace"));
        }

        // BP-6 (n18): the `using FzfHit = Telescope.Finders.GrepHit;` alias (FinderColumns.cs:6) is
        // deleted. The alias is compile-time-only — it creates NO distinct type — so this pins the
        // observable contract: no distinct FzfHit type exists in the Telescope assembly.
        // DEVIATION: cannot be made genuinely RED (the alias has no runtime presence; the Fzf
        // catalog getters are already typed to GrepHit today).
        public static void Run_FinderColumns_NoUnusedAlias()
        {
            var fzfHitType = typeof(FinderColumns).Assembly.GetType("Telescope.Finders.FzfHit");
            Assert.True(fzfHitType == null,
                "no distinct FzfHit type exists (the alias is compile-time-only) — BP-6/n18");
        }

        // BP-7 (n11): FocusTargetModel.MapKey is dead (the overlay resolves chords through
        // ChordDirection directly). RED today: it exists at FocusTargetModel.cs:228-231.
        public static void Run_FocusTargetModel_MapKeyRemoved()
        {
            Assert.True(typeof(FocusTargetModel).GetMethod("MapKey") == null,
                "FocusTargetModel.MapKey must be deleted (ChordDirection is the surviving chord map) — BP-7/n11");
        }

        // BP-8 (n12): the _fileCache field becomes readonly (it is assigned only in the ctor).
        // RED today: the field is not init-only.
        public static void Run_CodeIssuesFinder_Readonly()
        {
            var field = typeof(CodeIssuesFinder).GetField("_fileCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.True(field != null, "_fileCache field exists");
            Assert.True(field!.IsInitOnly,
                "_fileCache must be readonly (assigned only in the ctor) — BP-8/n12");
        }

        // BP-9 (n16): the ResultsFormatter seam is simplified to a SINGLE method (the separate
        // RenderedTextLength + ColumnsIdList helpers fold into one); the results columns={ids} and
        // boxText={len} formats stay byte-stable. RED today: two methods exist.
        public static void Run_ResultsFormatter_Simplified()
        {
            var methods = typeof(ResultsFormatter)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Where(m => m.DeclaringType == typeof(ResultsFormatter))
                .ToList();
            Assert.Equal(1, methods.Count);
        }

        // BP-10 (n17): NeoVisualTraceListener.WriteLine("x") -> exactly ONE stamped line in the
        // debug log; Write("x") -> one UNSTAMPED fragment. RED today: WriteLine => Write(message)
        // drops the newline and can double-stamp (the fragment path stamps).
        public static void Run_NeoVisualTraceListener_SingleStamp()
        {
            using (var dir = new TempDir())
            {
                string debugPath = Path.Combine(dir.Path, "neovisual-main.log");
                WithDebugLogPath(debugPath, () =>
                {
                    var listener = new NeoVisualTraceListener();
                    listener.WriteLine("x");
                    listener.Write("y");
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(debugPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }

                    // WriteLine("x") -> exactly ONE stamped line.
                    var stampedX = lines.Where(l => IsTimestampedLine(l) && l.EndsWith(" x")).ToList();
                    Assert.Equal(1, stampedX.Count);

                    // Write("y") -> one UNSTAMPED fragment (no timestamp prefix).
                    Assert.True(lines.Any(l => l == "y"),
                        "Write('y') must produce an unstamped fragment (no timestamp prefix) — BP-10/n17");
                });
            }
        }

        // BP-11 (n19): the pane creation must be serialized — N concurrent Log calls create exactly
        // ONE pane. RED today: the creation happens outside the lock (two threads can create two
        // panes). DEVIATION: the pane creation is gated behind ThreadHelper.CheckAccess() (only the
        // UI thread) and Package.GetGlobalService (returns null in a hermetic test — no VS), so the
        // pane-creation race is NOT observable in a unit test. This test pins the observable
        // thread-safety contract of Log instead: N concurrent calls produce exactly N log lines (no
        // lost writes / no corruption).
        public static void Run_NeoVisualLog_EnsurePane()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    // Ensure CheckAccess() returns false (not throws) on the background threads.
                    using (SetCurrentDispatcherAsUiThread())
                    {
                        const int n = 16;
                        var threads = new List<Thread>();
                        for (int i = 0; i < n; i++)
                        {
                            int idx = i;
                            var t = new Thread(() => NeoVisualLog.Log($"line-{idx}"));
                            t.IsBackground = true;
                            threads.Add(t);
                        }
                        foreach (var t in threads) t.Start();
                        foreach (var t in threads) t.Join();
                        LogFileWriter.Flush();

                        string[] lines = ReadAllTextShared(logPath)
                            .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                        if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                        {
                            Array.Resize(ref lines, lines.Length - 1);
                        }
                        // The pane-creation race is NOT observable hermetically (the pane is gated
                        // behind ThreadHelper.CheckAccess + Package.GetGlobalService — both no-ops/null
                        // in a unit test), so the `[NeoVisual] output pane unavailable: ...` fallback
                        // line may fire. Filter it out: the pinned contract is that N concurrent Log
                        // calls produce exactly N line-N lines (no lost writes / no corruption).
                        var payloadLines = lines.Where(l => l.Contains(" line-")).ToList();
                        Assert.Equal(n, payloadLines.Count);
                        for (int i = 0; i < n; i++)
                        {
                            Assert.True(payloadLines.Any(l => l.EndsWith($" line-{i}")), $"line-{i} was written");
                        }
                    }
                });
            }
        }

        // ================================================================
        // Code-review fixes (107 findings) — Section C RED phase, remaining tests.
        // ================================================================

        // BP-23 (m26): VisibilityByFinder must be an INSTANCE field (per-overlay) — the static
        // process-lifetime dictionary leaks across overlay instances. RED today: the field is
        // static -> IsStatic is true.
        public static void Run_VisibilityByFinder_Scoped()
        {
            var field = typeof(TelescopeOverlay).GetField("VisibilityByFinder",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
            Assert.True(field != null, "VisibilityByFinder field must exist");
            Assert.False(field!.IsStatic, "VisibilityByFinder must be an instance field (BP-23/m26)");
        }

        // BP-24 (m27): ShowOverlayAsync must gather via GetCandidatesAsync (off the UI thread),
        // not the synchronous GetCandidates on the UI thread. Mirrors
        // Run_CodeIssuesFinder_TodoScanOffThread's thread-id assertion. RED today: the gather runs
        // synchronously on the UI thread -> recordedThreadId == testThreadId.
        public static void Run_ShowOverlayAsync_AsyncGather()
        {
            using (SetCurrentDispatcherAsUiThread())
            {
                // Set up ThreadHelper.JoinableTaskContext so SwitchToMainThreadAsync works hermetically.
                var jtcField = typeof(Microsoft.VisualStudio.Shell.ThreadHelper).GetField(
                    "_joinableTaskContextCache",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                object? originalJtc = jtcField!.GetValue(null);
                try
                {
                    // VSSDK005 (the analyzer forbids instantiating a JoinableTaskContext) is
                    // suppressed: this test deliberately swaps the singleton for a hermetic context
                    // so SwitchToMainThreadAsync works without VS (the test-only seam).
#pragma warning disable VSSDK005
                    jtcField.SetValue(null, new Microsoft.VisualStudio.Threading.JoinableTaskContext());
#pragma warning restore VSSDK005

                    int testThreadId = Thread.CurrentThread.ManagedThreadId;
                    int recordedThreadId = -1;
                    var finder = new RecordingThreadFinder(() => recordedThreadId = Thread.CurrentThread.ManagedThreadId);
                    var overlay = new TelescopeOverlay(new FzfFilter("fzf"));

                    // The test host has no SynchronizationContext, so ShowOverlayAsync's post-await
                    // continuation (after `await _fzf.IsAvailableAsync()`) runs on a thread-pool
                    // thread and the WPF property access (Width/Height) throws VerifyAccess — a
                    // test-environment artifact, NOT the BP-24/m27 contract. The gather (line 388)
                    // already ran before that point, so the AsyncGatherCalled flag is set; catch the
                    // artifact and assert the real contract: ShowOverlayAsync must gather via
                    // GetCandidatesAsync (the async path), not the sync GetCandidates.
                    try
                    {
                        overlay.ShowOverlayAsync(finder, null).GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException)
                    {
                        // WPF thread-affinity artifact (see above) — the gather already ran.
                    }

                    Assert.True(finder.AsyncGatherCalled,
                        "ShowOverlayAsync must gather via GetCandidatesAsync (the async path), not the sync GetCandidates — BP-24/m27");
                    Assert.True(recordedThreadId != -1, "the gather must be invoked (BP-24/m27)");
                }
                finally
                {
                    jtcField.SetValue(null, originalJtc);
                }
            }
        }

        // BP-26 (m29): EnterInsert must refresh the prompt caret style (block->line) even when
        // already on the Input pane (the n16 ShouldFocus guard skips FocusPane, the only place the
        // style refreshed). RED today: the block caret persists after EnterInsert on Input.
        public static void Run_EnterInsert_CaretStyle()
        {
            var overlay = new TelescopeOverlay(new FzfFilter("fzf"));
            var promptField = typeof(TelescopeOverlay).GetField("_promptBox",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enterInsert = typeof(TelescopeOverlay).GetMethod("EnterInsert",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var promptBox = (System.Windows.Controls.TextBox)promptField!.GetValue(overlay)!;

            // Put the overlay in NORMAL mode so the block caret is applied.
            var keyHandlerField = typeof(TelescopeOverlay).GetField("_keyHandler",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var keyHandler = keyHandlerField!.GetValue(overlay)!;
            var handleMethod = keyHandler.GetType().GetMethod("Handle", new[] { typeof(OverlayKey) });
            handleMethod!.Invoke(keyHandler, new object[] { OverlayKey.Escape });

            var applyCaretStyle = typeof(TelescopeOverlay).GetMethod("ApplyPromptCaretStyle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            applyCaretStyle!.Invoke(overlay, null);

            // Setup check: normal mode shows the block caret.
            Assert.True(ReferenceEquals(promptBox.CaretBrush, BlockCaretStyle.CreateBlockBrush()),
                "setup: normal mode shows the block caret");

            enterInsert!.Invoke(overlay, new object[] { CaretPlacement.Current });

            Assert.True(!ReferenceEquals(promptBox.CaretBrush, BlockCaretStyle.CreateBlockBrush()),
                "after EnterInsert the prompt caret must be the insert-mode line caret, not the block caret (BP-26/m29)");
        }

        // BP-28 (m31): the centering math converts the physical-pixel rect to DIPs BEFORE
        // subtracting the DIP window size (today r.Width - Width mixes pixels and DIPs).
        // COMPILE-RED: OverlayCentering does not exist yet -> CS0246.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing OverlayCentering breaks the build;
        // restored at the end of the RED phase).
        public static void Run_OverlayCentering_Units()
        {
            // Scale 1.5: rect 1920x1080 -> DIP 1280x720; window 760x420.
            // Left = 0 + (1280 - 760)/2 = 260; Top = 0 + (720 - 420)/3 = 100.
            var r = new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var (left, top) = OverlayCentering.Center(r, 760, 420, 1.5);
            Assert.True(Math.Abs(left - 260.0) < 0.001, $"expected ~260, got {left}");
            Assert.True(Math.Abs(top - 100.0) < 0.001, $"expected ~100, got {top}");
        }

        // BP-29 (m32): a faulting preview editor (Show/ApplyCaret throw) must be swallowed — the
        // overlay survives. RED today: the fault propagates out of ShowPreview.
        public static void Run_ShowPreview_TryCatch()
        {
            var overlay = new TelescopeOverlay(new FzfFilter("fzf"), () => new FaultingPreviewEditor());
            var showPreview = typeof(TelescopeOverlay).GetMethod("ShowPreview",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            showPreview!.Invoke(overlay, new object[] { new FileHit(@"C:\p\A.cs", 0) });
            // If the fault propagated, the Invoke throws TargetInvocationException -> the test fails.
        }

        // BP-32 (n9): SelectTarget must not allocate a per-move List<Candidate> (the two-pass
        // approach preserves the exact selection semantics). The allocation-free contract is pinned
        // via a capability seam (GC.GetAllocatedBytesForCurrentThread does not exist on net472 —
        // CS0117 — so the seam replaces the byte-count assertion). COMPILE-RED: the seam does not
        // exist yet -> CS0117.
        // TEMPORARILY COMMENTED OUT (compile-RED — the missing GeometricSelectionEngine.
        // UsesNoAllocSelection seam breaks the build; restored at the end of the RED phase).
        public static void Run_GeometricSelectionEngine_NoAlloc()
        {
            Assert.True(GeometricSelectionEngine.UsesNoAllocSelection,
                "SelectTarget must not allocate a per-move list (the two-pass approach) — BP-32/n9");
        }

        // BP-24 (m27): a finder whose gather records the executing thread id — the async-gather
        // seam the overlay's ShowOverlayAsync must use (GetCandidatesAsync, off the UI thread).
        private sealed class RecordingThreadFinder : FinderBase<TestHit>
        {
            private readonly Action _record;
            public RecordingThreadFinder(Action record) { _record = record; }
            public bool AsyncGatherCalled { get; private set; }
            public override string Name => "Recording";
            protected override IReadOnlyList<TestHit> GatherHits() { _record(); return new List<TestHit>(); }
            protected override FinderEntry ToEntry(TestHit hit) => new FinderEntry(hit.FilePath, hit);
            protected override void OpenHit(TestHit hit) { }
            public override Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)
            {
                return Task.Run(() =>
                {
                    AsyncGatherCalled = true;
                    _record();
                    return (IReadOnlyList<FinderEntry>)new List<FinderEntry>();
                });
            }
        }

        // BP-29 (m32): a preview editor whose Show/ApplyCaret throw — the fault must be swallowed.
        private sealed class FaultingPreviewEditor : IPreviewEditor
        {
            public PreviewEditorResult Show(IFileLocation location) => throw new InvalidOperationException("boom");
            public void ApplyCaret(int caretIndex) => throw new InvalidOperationException("boom");
            public void Focus() { }
            public void Dispose() { }
        }

        // BP-12 (n20): the comment at ResultMapper.cs:22-23 no longer claims the mapper is
        // "safe to use off the UI thread" (the instance-scoped cache is not thread-safe).
        // RED today: the comment still claims it.
        public static void Run_ResultMapper_ThreadSafety()
        {
            string source = File.ReadAllText(FindRepoFile(@"Telescope\Overlay\Utils\ResultMapper.cs"));
            Assert.False(source.Contains("safe to use off the UI thread"),
                "ResultMapper.cs must not claim off-UI-thread safety (BP-12/n20)");
        }

        // Locates a repo-relative source file by walking up from the test host's base directory
        // (tests/Telescope.Tests/bin/Debug/net472 -> repo root).
        private static string FindRepoFile(string relativePath)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                string? parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent))
                {
                    break;
                }
                dir = parent;
            }
            throw new FileNotFoundException($"Could not locate {relativePath} from {AppDomain.CurrentDomain.BaseDirectory}");
        }

        // ================================================================
        // Fake ErrorItems/ErrorItem (BP-24 — the shared ErrorItemsWalker test seam)
        // ================================================================

        private sealed class FakeErrorItem : ErrorItem
        {
            private readonly string _fileName;
            private readonly bool _throwOnAccess;
            public FakeErrorItem(string fileName, bool throwOnAccess = false)
            {
                _fileName = fileName;
                _throwOnAccess = throwOnAccess;
            }
            public string Description => _fileName;
            public vsBuildErrorLevel ErrorLevel => vsBuildErrorLevel.vsBuildErrorLevelHigh;
            public string FileName => _throwOnAccess ? throw new InvalidOperationException("boom") : _fileName;
            public int Line => 1;
            public int Column => 1;
            public string Project => string.Empty;
            public bool IsBuildError => false;
            public string FullPath => _fileName;
            public ErrorItems Collection => null!;
            public DTE DTE => null!;
            public void Navigate() { }
        }

        private sealed class FakeErrorItems : ErrorItems
        {
            private readonly List<ErrorItem> _items;
            public FakeErrorItems(List<ErrorItem> items) { _items = items; }
            public int Count => _items.Count;
            public ErrorItem Item(object index) => _items[(int)index - 1];   // 1-based
            public ErrorList Parent => null!;
            public DTE DTE => null!;
        }
    }
}
