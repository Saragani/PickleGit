# History Tools

## Comparing branches or commits

PickleGit can diff any two points in history against each other — not just a commit against its
parent. Pick two branches, tags, or commits to compare and PickleGit shows the combined set of
changed files between them, each openable in the normal [diff view](04-diff-view.md). This is
useful for questions like "what will this branch bring in if I merge it?" or "what changed between
these two tags?" without checking either one out.

## Bisect

`git bisect` helps you find which commit introduced a regression by binary-searching through
history: you mark a known-good and a known-bad commit, and git checks out commits in between for
you to test, narrowing the range each time until it identifies the exact culprit commit.

PickleGit surfaces this as a guided workflow: start a bisect from the commit graph's context menu,
mark the current checkout as **Good** or **Bad** as you test it, and PickleGit checks out the next
commit to test automatically. A status banner shows you're mid-bisect and how many candidates
remain. When it converges, PickleGit shows you the first bad commit's summary; you can then exit
the bisect (returning to your original branch) at any point.
