using gView.Pipe.Cli;

return await PipeCliApp.RunAsync(args, Console.Out, Console.Error, Console.In, Environment.GetEnvironmentVariable);
