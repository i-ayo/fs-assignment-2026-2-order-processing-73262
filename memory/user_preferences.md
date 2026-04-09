---
name: User preferences
description: UI theme and branch naming preferences for this project
type: user
---

**Apple UI theme** — All frontends (CustomerPortal and AdminDashboard) must use an Apple-inspired design:
- Font: `-apple-system, BlinkMacSystemFont, "SF Pro Display", "Helvetica Neue"`
- Accent: `#007AFF` (Apple blue)
- Background: `#F2F2F7` (iOS system gray6)
- Cards: white, `border-radius: 12px`, subtle shadow
- Frosted-glass navbar: `backdrop-filter: saturate(180%) blur(20px)`
- No Bootstrap dark theme; override Bootstrap utilities toward Apple palette

**Why:** User explicitly requested Apple UI theme.

**Branch name is `master`** — not `main`. CI workflow targets `master`.

**Why:** User corrected this when `main` was accidentally used.
