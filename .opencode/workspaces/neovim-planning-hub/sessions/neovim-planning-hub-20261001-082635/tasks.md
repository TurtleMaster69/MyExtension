# Tasks — neovim-planning-hub-20261001-082635

## Research dispatch plan (Step 3)
1. trailmark-recon — whole-repo structural digest (fresh, for the plan's ground truth + rename blast radius).
2. arch-auditor (code-review fix verification) — verify the 73 findings against current code; produce fix direction + unit-test seam for each major + the regression findings; group minors/nits.
3. arch-auditor (restructure impact) — inventory every file per folder; classify main vs helper/util; propose the new folder structure + renames; identify the reference blast radius (which files reference each renamed type).

## Restructure design inputs (from the user)
- "new folder structure is better but main files and their helpers are still in same folders. separate them by functionality."
- "example navigation folder should contain navigation files and all utils should be in subfolder."
- "also rename the files and consequently the main class names to represent what they actually navigate."
- "do same of all other services, utils, functionalities."
