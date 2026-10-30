var builder = DistributedApplication.CreateBuilder(args);

// Keep PostgreSQL data across ordinary container and AppHost restarts.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();
var database = postgres.AddDatabase("marginalia");

builder.AddProject<Projects.Marginalia_Web>("web")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
