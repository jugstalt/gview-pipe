// optional dev containers: uncomment to start them alongside the Shell
#define ADD_MAILPIT
#define ADD_GITEA
#define ADD_IDENTITYSERVER
#define ADD_SFTP
#define ADD_GREENMAIL

// how users sign in, for the Shell (PIPE_*) and for the portal (PORTAL_*); at most one of the two per app, none: no login.
// OIDC signs in through the IdentityServer (needs ADD_IDENTITYSERVER), FORMS through fixed users of the AppHost.
// The settings go in as environment variables, which win over _config/auth.config; but lists (Users, Scopes) are merged
// by position, so move away a _config/auth.config of your own that has Users while you use PIPE_FORMS
#define PIPE_OIDC
//#define PIPE_FORMS
#define PORTAL_OIDC
//#define PORTAL_FORMS

#if PIPE_OIDC && PIPE_FORMS
#error PIPE_OIDC and PIPE_FORMS exclude each other: define only one
#endif
#if PORTAL_OIDC && PORTAL_FORMS
#error PORTAL_OIDC and PORTAL_FORMS exclude each other: define only one
#endif
#if (PIPE_OIDC || PORTAL_OIDC) && !ADD_IDENTITYSERVER
#error PIPE_OIDC and PORTAL_OIDC need ADD_IDENTITYSERVER
#endif

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
       .WithArgs("-c", "/usr/bin/entrypoint &\nfor i in $(seq 1 30); do\n  sleep 2\n  su git -c \"gitea admin user create --admin --username gitea --password gitea --email gitea@localhost --must-change-password=false\" && break\n  su git -c \"gitea admin user list\" | grep -q \" gitea \" && break\ndone\nwait")
       .WithContainerName("gview-pipe-gitea")
       .WithLifetime(ContainerLifetime.Persistent);

#endregion
#endif

#if ADD_SFTP
#region SFTP server (SFTP node and SFTP Trigger: localhost:2223, login pipe/pipe, folders in, out, done)

// atmoz/sftp: "user:password:uid:gid:folders" creates the user with the folders below its home (/in, /out, /done for SFTP)
builder.AddContainer("sftp", "atmoz/sftp", "latest")
       .WithEndpoint(port: 2223, targetPort: 22, name: "sftp", scheme: "tcp")
       .WithArgs("pipe:pipe:1001:100:in,out,done")
       // the host keys live in a volume, so the fingerprint in the credential stays valid when the container is created again
       .WithVolume("gview-pipe-sftp-keys", "/etc/ssh")
       .WithVolume("gview-pipe-sftp-home", "/home/pipe")
       .WithContainerName("gview-pipe-sftp")
       .WithLifetime(ContainerLifetime.Persistent);

#endregion
#endif

#if ADD_GREENMAIL
#region GreenMail (Email Trigger and Send Email: SMTP localhost:3025, IMAP localhost:3143, login pipe/pipe, address pipe@localhost)

// a mail server for tests: whatever is sent to pipe@localhost over SMTP can be read over IMAP; mails live in memory only
builder.AddContainer("greenmail", "greenmail/standalone", "latest")
       .WithEndpoint(port: 3025, targetPort: 3025, name: "smtp", scheme: "tcp")
       .WithEndpoint(port: 3143, targetPort: 3143, name: "imap", scheme: "tcp")
       .WithEnvironment("GREENMAIL_OPTS", "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.users=pipe:pipe@localhost")
       .WithContainerName("gview-pipe-greenmail")
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
               // viewer, operator, editor and admin of the Shell below; portal-demo is a portal role (ADR 0030)
               .AddUserRoles(["gview-pipe", "pipe-viewers", "pipe-operators", "pipe-editors", "pipe-admins", "pipe-samples-editors", "portal-demo"])
               // users to try them with; the name is an address, so it is the email of the user as well
               .WithUser("pipe-viewer@is.net", "viewer", ["gview-pipe", "pipe-viewers"])
               .WithUser("pipe-operator@is.net", "operator", ["gview-pipe", "pipe-operators"])
               .WithUser("pipe-editor@is.net", "editor", ["gview-pipe", "pipe-editors"])
               .WithUser("pipe-admin@is.net", "admin", ["gview-pipe", "pipe-admins"])
               // viewer in general, editor in the group samples (a role for one group: editor@samples)
               .WithUser("pipe-samples@is.net", "samples", ["gview-pipe", "pipe-viewers", "pipe-samples-editors"])
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
// the roles of the users above for the OIDC mode of the Shell (PIPE_OIDC, or "Mode": "oidc" in _config/auth.config); unused in other modes
shell.WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-viewers", "viewer")
     .WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-operators", "operator")
     .WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-editors", "editor")
     .WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-samples-editors", "editor@samples")
     .WithEnvironment("Pipe__Auth__Oidc__RoleMappings__pipe-admins", "admin");
#endif

#if PIPE_OIDC
// users of the IdentityServer sign in (ADR 0014); only those with the role gview-pipe enter, the rest is up to the mappings above
shell.WithEnvironment("Pipe__Auth__Mode", "oidc")
     .WithEnvironment("Pipe__Auth__Oidc__Authority", "https://localhost:8443")
     .WithEnvironment("Pipe__Auth__Oidc__ClientId", "local-gview-pipe")
     .WithEnvironment("Pipe__Auth__Oidc__ClientSecret", "secret")
     .WithEnvironment("Pipe__Auth__Oidc__RequiredRoles__0", "gview-pipe")
     // a user without a mapped role is only a viewer
     .WithEnvironment("Pipe__Auth__DefaultRole", "viewer");
#elif PIPE_FORMS
// fixed users (ADR 0014): name and password are the same; plain passwords log a warning on start, which is fine for development
shell.WithEnvironment("Pipe__Auth__Mode", "forms")
     .WithEnvironment("Pipe__Auth__DefaultRole", "viewer");
var formsRoles = new[] { "viewer", "operator", "editor", "admin" };
for (var i = 0; i < formsRoles.Length; i++)
{
    shell.WithEnvironment($"Pipe__Auth__Users__{i}__Name", formsRoles[i])
         .WithEnvironment($"Pipe__Auth__Users__{i}__Password", formsRoles[i])
         .WithEnvironment($"Pipe__Auth__Users__{i}__Role", formsRoles[i]);
}
// viewer in general, editor in the group samples
shell.WithEnvironment("Pipe__Auth__Users__4__Name", "samples")
     .WithEnvironment("Pipe__Auth__Users__4__Password", "samples")
     .WithEnvironment("Pipe__Auth__Users__4__Role", "viewer")
     .WithEnvironment("Pipe__Auth__Users__4__GroupRoles__samples", "editor");
#endif

var portal = builder.AddProject<Projects.gView_Pipe_Portal>("gview-pipe-portal")
       .WithEnvironment("Portal__BackendUrl", shell.GetEndpoint("https"))
       .WithEnvironment("Portal__ApiKey", portalKey)
       .WaitFor(shell);

#if PORTAL_OIDC
// users of the IdentityServer sign in; the role claim "role" gives them the portal roles (ADR 0030) and "email" their address
portal.WithEnvironment("Portal__Auth__Mode", "Oidc")
      .WithEnvironment("Portal__Auth__Oidc__Authority", "https://localhost:8443")
      .WithEnvironment("Portal__Auth__Oidc__ClientId", "local-gview-pipe-portal")
      .WithEnvironment("Portal__Auth__Oidc__ClientSecret", "secret");
#elif PORTAL_FORMS
// fixed users: portal has the portal role portal-demo (ADR 0030) and an address for mails (ADR 0029), guest has neither
portal.WithEnvironment("Portal__Auth__Mode", "Forms")
      .WithEnvironment("Portal__Auth__Users__0__Name", "portal")
      .WithEnvironment("Portal__Auth__Users__0__Password", "portal")
      .WithEnvironment("Portal__Auth__Users__0__Email", "portal@example.com")
      .WithEnvironment("Portal__Auth__Users__0__Roles__0", "portal-demo")
      .WithEnvironment("Portal__Auth__Users__1__Name", "guest")
      .WithEnvironment("Portal__Auth__Users__1__Password", "guest");
#endif

builder.Build().Run();
