using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE80;
using Telescope.Controller;
using Telescope.Filter;
using Telescope.Finders;
using Telescope.Logging;
using Telescope.Overlay;
using TestHarness;
using static TestHarness.TestScaffold;

namespace Telescope.Tests
{
    internal static class Program
    {
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
            // One row: the 2-char marker allowance + the cell length (no separator).
            Assert.Equal(7, ResultsFormatter.RenderedTextLength(new[] { new[] { "alpha" } }));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_MultiRow()
        {
            // Two rows: one '\n' separator between them (the legacy ToText layout).
            Assert.Equal(7, ResultsFormatter.RenderedTextLength(new[] { new[] { "a" }, new[] { "b" } }));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_MultiCell()
        {
            // One row, two cells: the cells concatenate (the legacy row layout had no column gap).
            Assert.Equal(5, ResultsFormatter.RenderedTextLength(new[] { new[] { "a", "bb" } }));
        }

        public static void Run_ResultsFormatter_RenderedTextLength_EmptyCells()
        {
            // Empty cell strings still count the marker + separator width.
            Assert.Equal(5, ResultsFormatter.RenderedTextLength(new[] { new[] { "" }, new[] { "" } }));
        }

        public static void Run_ResultsFormatter_ColumnsIdList_Empty()
        {
            // Empty/null set -> the empty string (the line reads "results columns=").
            Assert.Equal("", ResultsFormatter.ColumnsIdList(new string[] { }));
            Assert.Equal("", ResultsFormatter.ColumnsIdList(null!));
        }

        public static void Run_ResultsFormatter_ColumnsIdList_Single()
        {
            Assert.Equal("access", ResultsFormatter.ColumnsIdList(new[] { "access" }));
        }

        public static void Run_ResultsFormatter_ColumnsIdList_Multiple()
        {
            // Comma-joined, NO spaces, caller's order (the model supplies catalog order).
            Assert.Equal("access,file", ResultsFormatter.ColumnsIdList(new[] { "access", "file" }));
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
            var files = FinderColumns.ForFinder("Files", @"C:\proj");
            var hit = new FileHit(@"C:\proj\Services\Foo.cs", 0);
            Assert.Equal("Foo.cs", CellOf(files, "file", hit));
            Assert.Equal("Services", CellOf(files, "dir", hit));   // root-tail trimmed
            Assert.Equal(@"C:\proj\Services\Foo.cs", CellOf(files, "path", hit));
        }

        public static void Run_ResultsColumns_Files_DirWithoutRoot()
        {
            // No root: the dir cell is the FULL containing directory.
            var files = FinderColumns.ForFinder("Files");
            Assert.Equal(@"C:\proj\Services", CellOf(files, "dir", new FileHit(@"C:\proj\Services\Foo.cs", 0)));
            // A file directly in the root yields the empty cell.
            var rooted = FinderColumns.ForFinder("Files", @"C:\proj");
            Assert.Equal("", CellOf(rooted, "dir", new FileHit(@"C:\proj\Foo.cs", 0)));
            // A file outside the root yields the full directory.
            Assert.Equal(@"C:\other\Dir", CellOf(rooted, "dir", new FileHit(@"C:\other\Dir\Foo.cs", 0)));
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
            // Unknown -> unk (A-rev's 32-entry ImplMap — NOT verbatim lowercase). The real Kind strings
            // come from RoslynGatherers.cs:141-144 (TypeKind.ToString() / SymbolKind.ToString()) —
            // there is NO "Override" token; overrides arrive as Method/Property.
            Assert.Equal("cls", CellOf(impl, "kind", new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class")));
            Assert.Equal("inf", CellOf(impl, "kind", new ImplementationHit(@"C:\p\IShape.cs", 1, "IShape", "Interface")));
            Assert.Equal("func", CellOf(impl, "kind", new ImplementationHit(@"C:\p\Shape.cs", 4, "Draw", "Method")));
            Assert.Equal("prop", CellOf(impl, "kind", new ImplementationHit(@"C:\p\Shape.cs", 9, "Area", "Property")));
            Assert.Equal("str", CellOf(impl, "kind", new ImplementationHit(@"C:\p\P.cs", 1, "P", "Struct")));
            Assert.Equal("unk", CellOf(impl, "kind", new ImplementationHit(@"C:\p\X.cs", 1, "X", "Unknown")));

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
            // The remaining Roslyn TypeKind/SymbolKind union names + the 2 user-literal entries —
            // A-rev's complete 32-entry ImplMap (no raw long form may leak into the narrow column).
            Assert.Equal("del", KindAbbreviations.Implementation("Delegate"));
            Assert.Equal("errt", KindAbbreviations.Implementation("ErrorType"));
            Assert.Equal("typ", KindAbbreviations.Implementation("TypeParameter"));
            Assert.Equal("unk", KindAbbreviations.Implementation("Unknown"));
            Assert.Equal("arr", KindAbbreviations.Implementation("Array"));
            Assert.Equal("arrt", KindAbbreviations.Implementation("ArrayType"));
            Assert.Equal("dyn", KindAbbreviations.Implementation("Dynamic"));
            Assert.Equal("dynt", KindAbbreviations.Implementation("DynamicType"));
            Assert.Equal("mod", KindAbbreviations.Implementation("Module"));
            Assert.Equal("nmod", KindAbbreviations.Implementation("NetModule"));
            Assert.Equal("ptr", KindAbbreviations.Implementation("Pointer"));
            Assert.Equal("ptrt", KindAbbreviations.Implementation("PointerType"));
            Assert.Equal("sub", KindAbbreviations.Implementation("Submission"));
            Assert.Equal("fnptr", KindAbbreviations.Implementation("FunctionPointer"));
            Assert.Equal("fnpt", KindAbbreviations.Implementation("FunctionPointerType"));
            Assert.Equal("fld", KindAbbreviations.Implementation("Field"));
            Assert.Equal("loc", KindAbbreviations.Implementation("Local"));
            Assert.Equal("ntyp", KindAbbreviations.Implementation("NamedType"));
            Assert.Equal("ns", KindAbbreviations.Implementation("Namespace"));
            Assert.Equal("asm", KindAbbreviations.Implementation("Assembly"));
            Assert.Equal("lbl", KindAbbreviations.Implementation("Label"));
            Assert.Equal("par", KindAbbreviations.Implementation("Parameter"));
            Assert.Equal("rng", KindAbbreviations.Implementation("RangeVariable"));
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
            // The 30 REAL union values abbreviate to 30 DISTINCT cells (no within-column collision;
            // `inf` vs `info` is a CROSS-finder collision only — different finders, different columns).
            var union = new[] { "Class", "Interface", "Struct", "Enum", "Method", "Property", "Event",
                "Delegate", "ErrorType", "TypeParameter", "Unknown", "Array", "ArrayType", "Dynamic",
                "DynamicType", "Module", "NetModule", "Pointer", "PointerType", "Submission",
                "FunctionPointer", "FunctionPointerType", "Field", "Local", "NamedType", "Namespace",
                "Assembly", "Label", "Parameter", "RangeVariable" };
            var cells = union.Select(k => KindAbbreviations.Implementation(k)).ToList();
            Assert.Equal(30, cells.Count);
            Assert.Equal(30, cells.Distinct().Count());
        }

        public static void Run_ResultsColumns_ForFinder_UnknownName_Empty()
        {
            // An unknown IFinder.Name yields an EMPTY catalog (the pure model stays total; the
            // overlay falls back). Ordinal, case-sensitive: "files" must NOT match "Files".
            Assert.Equal(0, FinderColumns.ForFinder("Nope").Count);
            Assert.Equal(0, FinderColumns.ForFinder("files").Count);
        }

        public static void Run_ResultsColumns_WidthKinds()
        {
            // Section A BP-A1's invariant: exactly one Flexible column per finder at most;
            // Flexible => WidthChars 0; Fixed => WidthChars > 0. The exact WidthChars values are
            // Section-B tuning hints and are NEVER asserted here.
            foreach (var name in new[] { "Files", "Issues", "References", "Grep", "Fzf", "Implementation" })
            {
                var cols = FinderColumns.ForFinder(name);
                Assert.True(cols.Count > 0, $"{name} has a catalog");
                Assert.True(cols.Count(c => c.Width == ResultColumnWidth.Flexible) <= 1,
                    $"{name}: at most one Flexible column");
                foreach (var c in cols)
                {
                    Assert.True(
                        c.Width == ResultColumnWidth.Flexible ? c.WidthChars == 0 : c.WidthChars > 0,
                        $"{name}/{c.Id}: Flexible => WidthChars 0, Fixed => WidthChars > 0");
                }
            }
        }

        public static void Run_ColumnVisibility_ToggleOff()
        {
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.Equal("access,file", model.VisibleIdsJoined);

            // Toggle a visible column OFF -> it leaves VisibleIds; the others keep their order.
            Assert.True(model.Toggle("access"));
            Assert.Equal("file", model.VisibleIdsJoined);
        }

        public static void Run_ColumnVisibility_ToggleOn()
        {
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            // Toggle a hidden-by-default column ON (symbol was off) -> appended at its CATALOG
            // position (after file), not the end of the toggle order.
            Assert.True(model.Toggle("symbol"));
            Assert.Equal("access,file,symbol", model.VisibleIdsJoined);
        }

        public static void Run_ColumnVisibility_OrderStability()
        {
            // A toggled-off-then-on column returns to its CATALOG position (AC4's "order stable").
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.True(model.Toggle("access"));   // off   -> visible: file
            Assert.True(model.Toggle("column"));   // on    -> file,column
            Assert.True(model.Toggle("text"));     // on    -> file,column,text
            Assert.Equal("file,column,text", model.VisibleIdsJoined);

            // access (catalog index 0) comes back BEFORE file — a naive append-to-end
            // implementation would yield "file,column,text,access" and fail here.
            Assert.True(model.Toggle("access"));
            Assert.Equal("access,file,column,text", model.VisibleIdsJoined);
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
            Assert.Equal("file", model.VisibleIdsJoined);   // unchanged

            // Hiding is possible again once another column is visible.
            Assert.True(model.Toggle("symbol"));
            Assert.True(model.Toggle("file"));
            Assert.Equal("symbol", model.VisibleIdsJoined);
        }

        public static void Run_ColumnVisibility_UnknownIdNoOp()
        {
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.False(model.Toggle("no-such-column"));
            Assert.Equal("access,file", model.VisibleIdsJoined);   // unchanged, no throw
        }

        public static void Run_ColumnVisibility_IdFormat()
        {
            // The "[Telescope] results columns=<ids>" literal (D5/M-M7): the id list is
            // comma-separated with NO spaces, in the model's visible (catalog) order. The
            // overlay's log line (Section B) must embed EXACTLY VisibleIdsJoined.
            var model = new ColumnVisibilityModel(FinderColumns.ForFinder("References"));
            Assert.Equal("access,file", model.VisibleIdsJoined);
            Assert.True(model.Toggle("symbol"));
            Assert.Equal("access,file,symbol", model.VisibleIdsJoined);
        }

        public static void Run_ResultRowCells_OrderedCells()
        {
            // The row's cells are computed from the entry's PAYLOAD via the VISIBLE columns'
            // getters — one cell per visible column, in VISIBLE-column order (a hidden middle
            // column shifts the cells left). D3: presentation-only; Display is untouched.
            var visible = FinderColumns.ForFinder("References").Where(c => c.DefaultVisible).ToList();
            var hit = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            var cells = ResultRowCells.Compute(new FinderEntry("Value (write) Writer.cs:5:5 — Shared.Value = 1;", hit), visible);

            Assert.Equal(2, cells.Count);
            Assert.Equal("W", cells[0]);          // D2a: write -> W (Section A rev 1)
            Assert.Equal("Writer.cs", cells[1]);
        }

        public static void Run_ResultRowCells_NullPayloadEmptyCells()
        {
            // A payload-less entry (the E("alpha") shape) must not crash the row computation:
            // every cell is the empty string.
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            var cells = ResultRowCells.Compute(new FinderEntry("alpha"), visible);
            Assert.Equal(2, cells.Count);
            Assert.True(cells.All(c => c.Length == 0), "every cell is empty for a null payload");
        }

        public static void Run_ResultRowCells_ForeignPayloadEmptyCells()
        {
            // A payload of the WRONG hit type yields empty cells — never a throw (the getters'
            // Cell<THit> type test).
            var visible = FinderColumns.ForFinder("References").Where(c => c.DefaultVisible).ToList();
            var cells = ResultRowCells.Compute(new FinderEntry("a file row", new FileHit(@"C:\p\A.cs", 0)), visible);
            Assert.Equal(2, cells.Count);
            Assert.True(cells.All(c => c.Length == 0), "a foreign payload yields empty cells");
        }

        public static void Run_ResultRowCells_NullEntryEmptyCells()
        {
            var visible = FinderColumns.ForFinder("Files").Where(c => c.DefaultVisible).ToList();
            var cells = ResultRowCells.Compute(null, visible);
            Assert.Equal(2, cells.Count);
            Assert.True(cells.All(c => c.Length == 0), "a null entry yields empty cells");
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
                    }
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

                    // A missing fzf must fall back to the full list AND log the failure (M6: today the
                    // catch is silent — no [Telescope] line is emitted).
                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha"), "fallback returns the full candidate list");
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

                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha"), "timeout falls back to the full candidate list");
                    Assert.True(fzf.AwaitedReadCount >= 2,
                        "the timeout path must observe both ReadToEndAsync tasks (AwaitedReadCount >= 2)");
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
            Assert.Equal(OverlayAction.EnterInsertAppend, h.Handle(OverlayKey.A));
            Assert.False(h.IsNormalMode, "'a' returns to insert mode (append caret)");
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

            // A -> EnterInsertAppend (caret at end).
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.Equal(OverlayAction.EnterInsertAppend, h.Handle(OverlayKey.A));

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
        // RED (n11/BP-46): `TryDispatch` is still a separate 25-line wrapper class today —
        // the merged `TextMotionDispatcher.Handle(...)` surface does not exist yet ->
        // compile error (CS0117: 'TextMotionDispatcher' does not contain a definition for
        // 'Handle'). The union h/l/j/k/w/b/e/0/$/gg/G + a/A/I. The $ drift fix: bare D4
        // (no shift) is NOT a motion and must NOT LineEnd.
        // ================================================================

        public static void Run_TextMotionDispatcher_DollarWithoutShiftNotHandled()
        {
            // The $ drift fix: in the preview surface a bare D4 currently LineEnds; the shared
            // dispatch must require Shift for $ (D4), so a bare D4 returns false and does nothing.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            bool handled = TextMotionDispatcher.Handle(Key.D4, false, n, out _);
            Assert.False(handled, "bare $ (D4 without shift) is not a motion");
            Assert.Equal(0, n.Caret); // must NOT LineEnd
        }

        public static void Run_TextMotionDispatcher_DollarWithShiftLineEnds()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            bool handled = TextMotionDispatcher.Handle(Key.D4, true, n, out _);
            Assert.True(handled, "$ (D4 with shift) is handled");
            Assert.Equal(3, n.Caret); // end of "abc"
        }

        public static void Run_TextMotionDispatcher_MotionsMapToNavigator()
        {
            // H -> Left
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Handle(Key.H, false, n, out _));
            Assert.Equal(1, n.Caret);

            // L -> Right
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Handle(Key.L, false, n, out _));
            Assert.Equal(3, n.Caret);

            // W -> NextWord
            n.SetText("one two");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.Handle(Key.W, false, n, out _));
            Assert.Equal(4, n.Caret);

            // B -> PrevWord
            n.MoveTo(4);
            Assert.True(TextMotionDispatcher.Handle(Key.B, false, n, out _));
            Assert.Equal(0, n.Caret);

            // E -> EndWord
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.Handle(Key.E, false, n, out _));
            Assert.Equal(3, n.Caret);

            // J -> Down
            n.SetText("a\nb");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.Handle(Key.J, false, n, out _));
            Assert.Equal(2, n.Caret);

            // K -> Up
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Handle(Key.K, false, n, out _));
            Assert.Equal(0, n.Caret);

            // D0 -> LineStartHome
            n.SetText("abc\ndef");
            n.MoveTo(5);
            Assert.True(TextMotionDispatcher.Handle(Key.D0, false, n, out _));
            Assert.Equal(4, n.Caret);

            // G (bare) -> Top
            n.MoveTo(5);
            Assert.True(TextMotionDispatcher.Handle(Key.G, false, n, out _));
            Assert.Equal(0, n.Caret);

            // G (shift) -> Bottom
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.Handle(Key.G, true, n, out _));
            Assert.Equal(7, n.Caret);
        }

        public static void Run_TextMotionDispatcher_InsertPlacements()
        {
            // A (bare) -> InsertAfter, placement Current.
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            CaretPlacement? placement;
            Assert.True(TextMotionDispatcher.Handle(Key.A, false, n, out placement));
            Assert.Equal(CaretPlacement.Current, placement);
            Assert.Equal(3, n.Caret);

            // A (shift) -> InsertEnd, placement End.
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Handle(Key.A, true, n, out placement));
            Assert.Equal(CaretPlacement.End, placement);
            Assert.Equal(5, n.Caret);

            // I (shift) -> InsertStart, placement Start.
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Handle(Key.I, true, n, out placement));
            Assert.Equal(CaretPlacement.Start, placement);
            Assert.Equal(0, n.Caret);

            // I (bare) -> not handled (generic insert lives in the overlay state machine).
            n.MoveTo(2);
            Assert.False(TextMotionDispatcher.Handle(Key.I, false, n, out placement));
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
            Assert.False(PromptMotionRouter.ShouldConsume(Key.A, false, out placement),
                "bare a is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.AfterCaret, placement);

            // A (shift) -> InsertEnd, placement End.
            Assert.False(PromptMotionRouter.ShouldConsume(Key.A, true, out placement),
                "shift+A is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.End, placement);

            // i (bare) -> generic insert at the current position (OverlayKey.I -> Current).
            Assert.False(PromptMotionRouter.ShouldConsume(Key.I, false, out placement),
                "bare i is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.Current, placement);

            // I (shift) -> InsertStart, placement Start.
            Assert.False(PromptMotionRouter.ShouldConsume(Key.I, true, out placement),
                "shift+I is an insert placement, not a prompt motion");
            Assert.Equal(CaretPlacement.Start, placement);
        }

        public static void Run_PromptMotionRouter_MotionsConsumed()
        {
            // Motions ARE consumed by the prompt/preview motion handler (placement null).
            Assert.True(PromptMotionRouter.ShouldConsume(Key.H, false, out _),
                "h is a prompt motion");
            Assert.True(PromptMotionRouter.ShouldConsume(Key.W, false, out _),
                "w is a prompt motion");
            Assert.True(PromptMotionRouter.ShouldConsume(Key.D4, true, out _),
                "shift+4 ($) is a prompt motion");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.D4, false, out _),
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
            Assert.False(PromptMotionRouter.ShouldConsume(Key.J, false, out _),
                "j must not be consumed in the prompt (selection navigation)");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.K, false, out _),
                "k must not be consumed in the prompt (selection navigation)");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.G, false, out _),
                "g must not be consumed in the prompt (gg -> Top is a selection move)");
            Assert.False(PromptMotionRouter.ShouldConsume(Key.G, true, out _),
                "G must not be consumed in the prompt (Bottom is a selection move)");
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

        public static void Run_FileFinder_EnumeratesCandidates()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                string b = Path.Combine(dir.Path, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => new[] { a, b }, _ => { });
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
                var finder = new FileFinder(() => new[] { a }, p => opened = p);

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
                var finder = new FileFinder(() => new[] { missing }, _ => opened++);

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

        public static void Run_FileFinder_UsesProjectFileCache()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// a");

                int count = 0;
                var cache = new ProjectFileCache();
                var finder = new FileFinder(cache, () => { count++; return new[] { a }; }, _ => { });

                finder.GetCandidates();
                finder.GetCandidates();

                // The enumerate delegate must run ONCE across two gathers — the cache serves the
                // second GetCandidates (today FileFinder re-enumerates per gather).
                Assert.Equal(1, count);
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

        public static void Run_HierarchyWalker_FirstFileEndingWith()
        {
            // FirstFileEndingWith returns the first matching file in tree order, and the
            // extension comparison is OrdinalIgnoreCase (".CS" uppercase matches ".cs").
            using (var dir = new TempDir())
            {
                string txt = Path.Combine(dir.Path, "B.txt");
                string a = Path.Combine(dir.Path, "A.cs");
                string c = Path.Combine(dir.Path, "C.CS");
                File.WriteAllText(txt, "// t");
                File.WriteAllText(a, "// a");
                File.WriteAllText(c, "// c");

                var root = new FakeNode(null,
                    new FakeNode(txt),
                    new FakeNode(a),
                    new FakeNode(c));

                Assert.Equal(a, HierarchyWalker.FirstFileEndingWith(new[] { root }, ".cs"));
            }
        }

        public static void Run_HierarchyWalker_EmptyTree()
        {
            var files = HierarchyWalker.EnumerateFiles(new FakeNode[0]);
            Assert.Equal(0, files.Count);
            Assert.Equal(null, HierarchyWalker.FirstFileEndingWith(new FakeNode[0], ".cs"));
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

                var finder = new CodeIssuesFinder(() => new[] { a, b }, _ => { });
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

                var finder = new CodeIssuesFinder(() => new[] { a }, _ => { });
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
                var finder = new CodeIssuesFinder(() => new[] { a }, issue => opened = issue);
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

                var finder = new CodeIssuesFinder(() => new[] { a }, _ => { });
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

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates("").Count);
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

                var finder = new GrepFinder(() => new[] { a, b }, _ => { });
                // Case-insensitive substring: "needle" matches BOTH "NEEDLE here" (line 2) and
                // "needle again" (line 4); the non-matching "middle" line and B.cs are excluded.
                var entries = finder.GetCandidates("needle");

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

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entry = finder.GetCandidates("NEEDLE")[0];
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

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entry = finder.GetCandidates("NEEDLE")[0];
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
                var finder = new GrepFinder(() => new[] { a }, hit => opened = hit);
                var entry = finder.GetCandidates("NEEDLE")[0];

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

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entries = finder.GetCandidates("NEEDLE");
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
                var finder = new GrepFinder(cache, () => { count++; return new[] { a }; }, _ => { });

                finder.GetCandidates("NEEDLE");
                finder.GetCandidates("NEEDLE2");

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

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var syncHits = finder.GetCandidates("needle");

                // M4: the per-file content scan must return the SAME hits when it runs off-thread
                // (the fix moves the scan onto a background task and marshals only the results
                // back; the DTE enumeration stays on the UI thread). NOTE: this may PASS against
                // the current code — the hermetic path has no thread affinity — in which case the
                // off-thread seam is the fix (wiring-is-the-fix, not a RED).
                IReadOnlyList<FinderEntry> offThreadHits = null;
                Task.Run(() => offThreadHits = finder.GetCandidates("needle")).GetAwaiter().GetResult();

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
                    var finder = new GrepFinder(() => new[] { "a" }, _ => { });

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

                var finder = new GrepFinder(() => new[] { a }, _ => { });

                Assert.True(finder.IsQueryDriven, "GrepFinder is query-driven (GetCandidates(query))");
                Assert.True(finder.GetCandidates("").Count == 0, "empty query -> no candidates (short-circuit)");
                var hits = finder.GetCandidates("alpha");
                Assert.Equal(1, hits.Count);
                Assert.True(hits[0].Display.Contains("A.cs"), "hit display names the file");
            }
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

            public int FilterCalls { get; private set; }

            public FakeFzfEngine(bool available, Func<IEnumerable<string>, string, IReadOnlyList<string>>? filter = null)
            {
                _available = available;
                _filter = filter ?? ((candidates, query) =>
                    candidates.Where(c => c.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList());
            }

            public Task<bool> IsAvailableAsync() => Task.FromResult(_available);

            public Task<IReadOnlyList<string>> FilterAsync(IEnumerable<string> candidates, string query, CancellationToken ct)
            {
                FilterCalls++;
                return Task.FromResult(_filter(candidates, query));
            }
        }

        public static void Run_FzfLineMapper_ExactLineMapsToLineNumber()
        {
            var fileLines = new[] { "alpha", "NEEDLE here", "beta" };
            var matched = new[] { "NEEDLE here" };

            var lines = FzfLineMapper.Map(fileLines, matched);

            Assert.Equal(1, lines.Count);
            Assert.Equal(2, lines[0]);
        }

        public static void Run_FzfLineMapper_DuplicateLinesMapOrdinal()
        {
            // Two identical lines in one file must map to their two distinct 1-based line numbers
            // in matched order (ordinal consumption), not both to the first occurrence (AC5).
            var fileLines = new[] { "dup", "x", "dup" };
            var matched = new[] { "dup", "dup" };

            var lines = FzfLineMapper.Map(fileLines, matched);

            Assert.Equal(2, lines.Count);
            Assert.Equal(1, lines[0]);
            Assert.Equal(3, lines[1]);
        }

        public static void Run_FzfLineMapper_UnknownLineSkipped()
        {
            // A matched string with no remaining unconsumed identical line is skipped (no bogus line).
            var fileLines = new[] { "alpha", "beta" };
            var matched = new[] { "not-in-file" };

            var lines = FzfLineMapper.Map(fileLines, matched);

            Assert.Equal(0, lines.Count);
        }

        public static void Run_FzfFinder_EmptyQueryReturnsZeroCandidates()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new FzfFinder(() => new[] { a }, _ => { }, new FakeFzfEngine(true));
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
                    var finder = new FzfFinder(() => new[] { "a" }, _ => { }, new FakeFzfEngine(true));

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
                    var finder = new FzfFinder(() => new[] { "a" }, _ => { }, engine);

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

                    var finder = new FzfFinder(() => new[] { a }, _ => { }, new FakeFzfEngine(true));
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

                var finder = new FzfFinder(() => new[] { a }, _ => { }, new FakeFzfEngine(true));
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

                var finder = new FzfFinder(() => new[] { a }, _ => { }, new FakeFzfEngine(true));
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
                var finder = new FzfFinder(() => new[] { a }, hit => opened = hit, new FakeFzfEngine(true));
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

                var finder = new FzfFinder(() => new[] { a }, _ => { }, new FakeFzfEngine(true));
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
                var finder = new FzfFinder(cache, () => { count++; return new[] { a }; }, _ => { }, new FakeFzfEngine(true));

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

                var finder = new FzfFinder(() => new[] { a }, _ => { }, new FakeFzfEngine(true));

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
                    var finder = new FzfFinder(() => new[] { "a" }, _ => { }, new FakeFzfEngine(true));

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
                    var finder = new FzfFinder(() => new[] { a }, _ => { }, engine);

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

        // ================================================================
        // IFinder query fold — GetCandidates(string) + IsQueryDriven (BP-8/L6)
        // RED: `GetCandidates(string)` / `IsQueryDriven` do not exist on IFinder
        //      -> compile error (the fold has not happened)
        // ================================================================

        public static void Run_GetCandidates_DefaultQuery_MatchesNoArg()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                string b = Path.Combine(dir.Path, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => new[] { a, b }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(noArg.Count, emptyArg.Count);
                Assert.Equal(noArg[0].Display, emptyArg[0].Display);
                Assert.Equal(noArg[1].Display, emptyArg[1].Display);
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
                IFinder finder = new GrepFinder(() => new[] { a }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(0, noArg.Count);
                Assert.Equal(0, emptyArg.Count);
            }
        }

        public static void Run_GetCandidates_IsQueryDriven()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// x\n");

                IFinder grep = new GrepFinder(() => new[] { a }, _ => { });
                IFinder files = new FileFinder(() => new[] { a }, _ => { });

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
            Assert.Equal("[GlobalKeyboard] ", DiagnosticLog.GlobalKeyboard);
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
        // FocusTargetModel — pure focus-target state machine (M34)
        // RED: `FocusTargetModel`/`FocusTarget`/`FocusTargetAction` + `OverlayKey.CtrlH`/`CtrlL`
        // don't exist -> compile error (CS0246/CS0103/CS0117)
        // ================================================================

        public static void Run_FocusTarget_StartsWithList()
        {
            var model = new FocusTargetModel();
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_CtrlLMovesToPreview()
        {
            var model = new FocusTargetModel();
            var action = model.Handle(OverlayKey.CtrlL);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_CtrlHReturnsToList()
        {
            var model = new FocusTargetModel();
            model.Handle(OverlayKey.CtrlL); // move to Preview first
            var action = model.Handle(OverlayKey.CtrlH);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_EscapeInPreviewReturnsToList()
        {
            var model = new FocusTargetModel();
            model.Handle(OverlayKey.CtrlL); // move to Preview first
            var action = model.Handle(OverlayKey.Escape);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_EscapeInListUnchanged()
        {
            var model = new FocusTargetModel();
            var action = model.Handle(OverlayKey.Escape);
            Assert.Equal(FocusTargetAction.None, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_ResetOnOpen()
        {
            var model = new FocusTargetModel();
            model.Handle(OverlayKey.CtrlL); // move to Preview
            model.Reset();
            Assert.Equal(FocusTarget.List, model.Current);
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
    }
}
