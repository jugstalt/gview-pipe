var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.gView_Pipe_Shell>("gview-pipe");

builder.Build().Run();
