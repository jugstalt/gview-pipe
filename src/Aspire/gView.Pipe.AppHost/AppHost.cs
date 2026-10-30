// optional dev containers: uncomment to start them alongside the Shell
#define ADD_MAILPIT
#define ADD_GITEA
#define ADD_IDENTITYSERVER

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
               .AddIdentityResources(["openid", "profile", "role"])
               .AddClient(ClientType.WebApplication,
                          "local-gview-pipe", "secret",
                          "https://localhost:7240",
                          [
                              "openid", "profile", "role"
                          ])
       )
       .Build()
       .WithContainerName("gview-pipe-identityserver-net")
       .WithLifetime(ContainerLifetime.Persistent);

#endregion
#endif

builder.AddProject<Projects.gView_Pipe_Shell>("gview-pipe");

builder.Build().Run();
