// optional dev containers: uncomment to start them alongside the Shell
#define ADD_MAILPIT
#define ADD_GITEA
#define ADD_IDENTITYSERVER
// the portal signs users in with the IdentityServer (needs ADD_IDENTITYSERVER); without it the portal has no login
#define PORTAL_OIDC

using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

#if ADD_MAILPIT
#region Mailpit (Send Mail node: SMTP localhost:1025, UI http://localhost:8025)

builder.AddMailPit("mailpit", httpPort: 8025, smtpPort: 1025)
       .WithContainerName("gview-pipe-mailpit")
       .WithLifetime(ContainerLifetime.Persistent);

#endregion
#endif

#if ADD_GITEA
#region Gitea (Run Scripts node: http://localhost:3000, ssh localhost:2222, login gitea/gitea)

builder.AddContainer("gitea", "gitea/gitea", "latest")
       .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
       .WithEndpoint(port: 2222, targetPort: 22, name: "ssh", scheme: "tcp")
       .WithEnvironment("GITEA__database__DB_TYPE", "sqlite3")
       .WithEnvironment("GITEA__security__INSTALL_LOCK", "true")
       .WithEnvironment("GITEA__server__ROOT_URL", "http://localhost:3000/")
       .WithEnvironment("GITEA__server__SSH_PORT", "2222")
       .WithVolume("gview-pipe-gitea", "/data")
       // starts Gitea and creates the admin once it is up (fails harmlessly when the user already exists)
       .WithEntrypoint("/bin/sh")
       .WithArgs("-c", """
           /usr/bin/entrypoint &
           for i in $(seq 1 30); do
             sleep 2
             su git -c "gitea admin user create --admin --username gitea --password gitea --email gitea@localhost --must-change-password=false" && break
             su git -c "gitea admin user list" | grep -q " gitea " && break
           done
           wait
           """)
       .WithContainerName("gview-pipe-gitea")
       .WithLifetime(ContainerLifetime.Persistent);

#endregion
#endif

#if ADD_IDENTITYSERVER
#region IdentityServerNET (https://localhost:8443, login admin/admin)

var identityServer = builder.AddIdentityServerNET("is-net-dev", httpsPort: 8443)
       .WithConfiguration(config =>
       {
           config
                .RememberLoginDefaultValue(true)
                .DenyForgotPasswordChallange()
                .DenyManageAccount();
       })
       .WithMigrations(migrations =>
            migrations
               .AddAdminPassword("admin")
               .AddIdentityResources(["openid", "profile", "role", "email"])
               .AddClient(ClientType.WebApplication,
                          "local-gview-pipe", "secret",
                          "https://localhost:7240",
                          [
                              "openid", "profile", "role"
                          ])
               // the portal (https://localhost:7250): redirect URIs <portal>/signin-oidc and <portal>/signout-callback-oidc
               .AddClient(ClientType.WebApplication,
                          "local-gview-pipe-portal", "secret",
                          "https://localhost:7250",
                          [
                              // email: the address for the mail when a run is done
                              "openid", "profile", "role", "email"
                          ])
               // roles: gview-pipe lets a user into the Shell (RequiredRoles); the pipe-* roles are mapped to the roles
               // viewer, editor and admin of the Shell below; portal-demo is a portal role (ADR 0030)
               .AddUserRoles(["gview-pipe", "pipe-viewers", "pipe-editors", "pipe-admins", "portal-demo"])
               // users to try them with; the name is an address, so it is the email of the user as well
               .WithUser("pipe-viewer@is.net", "viewer", ["gview-pipe", "pipe-viewers"])
               .WithUser("pipe-editor@is.net", "editor", ["gview-pipe", "pipe-editors"])
               .WithUser("pipe-admin@is.net", "admin", ["gview-pipe", "pipe-admins"])
               .WithUser("portal@is.net", "portal", ["portal-demo"])
       )
       .Build()
       // the image of 7.26.2902 has a certificate that expired on 2026-08-26; drop this once the package catches up
       .WithImageTag("7.26.4301")
       .WithContainerName("gview-pipe-identityserver-net")
       .WithLifetime(ContainerLifetime.Persistent);

#endregion
#endif

// the backend, and the user portal in front of it (ADR 0028)
// the key of the portal is made up on every start (a secret parameter, never in the code); the Shell puts it into its
// key store in development, so the portal can be tried without the page API keys
var portalKey = builder.AddParameter("portal-key", () => "pipe_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), secret: true);

var shell = builder.AddProject<Projects.gView_Pipe_Shell>("gview-pipe")
       .WithEnvironment("Pipe__DevPortalKey", portalKey);

#if ADD_IDENTITYSERVER
// the roles of the users above for the OIDC mode of the Shell (_config/auth.config: "Mode": "oidc"); unused in other modes
shell.WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-viewers", "viewer")
     .WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-editors", "editor")
     .WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-admins", "admin");
#endif

var portal = builder.AddProject<Projects.gView_Pipe_Portal>("gview-pipe-portal")
       .WithEnvironment("Portal__BackendUrl", shell.GetEndpoint("https"))
       .WithEnvironment("Portal__ApiKey", portalKey)
       .WaitFor(shell);

#if PORTAL_OIDC && ADD_IDENTITYSERVER
// users of the IdentityServer sign in; the role claim "role" gives them the portal roles (ADR 0030) and "email" their address
portal.WithEnvironment("Portal__Auth__Mode", "Oidc")
      .WithEnvironment("Portal__Auth__Oidc__Authority", "https://localhost:8443")
      .WithEnvironment("Portal__Auth__Oidc__ClientId", "local-gview-pipe-portal")
      .WithEnvironment("Portal__Auth__Oidc__ClientSecret", "secret");
#endif

builder.Build().Run();
