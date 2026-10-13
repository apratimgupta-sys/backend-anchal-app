# Anchal rollback

These steps operate only on the standalone Anchal deployment. Do not restart,
reload, edit, or remove any Apna Ranchi process or Nginx location.

1. Select the previous verified Anchal release directory under
   `$ANCHAL_DEPLOY_DIR/releases/`.
2. Load the protected Anchal-only environment file and point PM2 at that release:

   ```sh
   export ANCHAL_RELEASE_DIR="$ANCHAL_DEPLOY_DIR/releases/<verified-release>"
   set -a
   . "$ANCHAL_ENV_FILE"
   set +a
   pm2 startOrReload deploy/ecosystem.anchal.config.cjs --only anchal-api --update-env
   ```

3. Verify the rolled-back service without following redirects:

   ```sh
   python3 deploy/anchal-smoke.py --base-url "$ANCHAL_BASE_URL" --mode enabled
   ```

4. If the Anchal service must be disabled, remove or revert only the three
   Anchal locations from the Nginx include: `/v1/api/anchal/`,
   `/health/anchal`, and `/anchal/`. Validate and reload Nginx according to
   the host's established change process. Do not alter generic `/api/`, `/`,
   or other Apna Ranchi locations.

Database migrations are not automatically reversed. Restore the independent
Anchal database only through its separately approved backup and migration plan.
