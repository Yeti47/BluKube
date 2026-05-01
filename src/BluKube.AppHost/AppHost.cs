var builder = DistributedApplication.CreateBuilder(args);

var server = builder.AddProject<Projects.BluKube_Server>("blukube-server");

builder.Build().Run();
