# Pull Requests

PickleGit can open a pull/merge request directly against your repository's host (GitHub or
GitLab, including self-hosted GitHub Enterprise / GitLab instances configured in
[Settings → Integrations](09-settings.md)) without leaving the app.

After pushing a branch, use the **Create Pull Request** action (available from the branch's
context menu, or after a push completes) to:

1. Pick the base branch to merge into.
2. Fill in a title and description — PickleGit pre-fills these from your branch's commits when it
   can.
3. Submit — the request is created through the host's API using your configured credentials.

## Authentication

Pull request creation needs a personal access token for the target host, configured once under
Settings → Integrations. For a self-hosted GitHub Enterprise or GitLab instance, add the domain
there first (with the correct provider kind selected) so PickleGit knows which API shape to use
for that host.
