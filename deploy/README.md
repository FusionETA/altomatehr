# deploy/

Reference copies of the server-side pieces of production on the droplet. They are
**not** used from here — the installed copies are what run. When you change one,
change it here too and reinstall it on the server, so the two don't drift.

| File here | Installed at | What it is |
|---|---|---|
| `altomatehr-v2-deploy.sh` | `/usr/local/bin/altomatehr-v2-deploy.sh` | The deploy run by `.github/workflows/deploy.yml` over SSH |
| `nginx/hr-guide.altomate.io.conf` | `/etc/nginx/sites-available/hr-guide.altomate.io` | Serves `user-guide/` at https://hr-guide.altomate.io |

## How a deploy works

1. A PR is merged to `main` → the **CI** workflow runs.
2. If CI passes, the **Deploy** workflow SSHes to the droplet with the
   `DEPLOY_SSH_KEY` secret. In root's `authorized_keys` that key is pinned with
   `restrict,command="/usr/local/bin/altomatehr-v2-deploy.sh"`, so it can only
   run the deploy script.
3. The script fast-forwards `/opt/altomatehr-v2` to `origin/main`, rebuilds the
   API container (EF migrations run on API startup), rebuilds the frontend into
   `dist.next` and swaps it in, then health-checks both. Log:
   `/var/log/altomatehr-v2-deploy.log`.

The script refuses to run if the server checkout has uncommitted edits to tracked
files — commit through git rather than editing on the server.

To redeploy by hand: Actions → Deploy → Run workflow, or on the server
`/usr/local/bin/altomatehr-v2-deploy.sh`.

## Reinstalling

```bash
# deploy script — copy, don't symlink: a symlink into the checkout would be
# rewritten by the script's own `git merge` while bash is still reading it.
install -m 755 deploy/altomatehr-v2-deploy.sh /usr/local/bin/altomatehr-v2-deploy.sh

# nginx site — keep certbot's TLS lines when copying over the installed file
nginx -t && systemctl reload nginx
```
