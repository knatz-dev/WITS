$env:DOTNET_ENVIRONMENT = "Development"
$database = Join-Path $PWD "APP_DATA/WITS.Development.db"
$env:ConnectionStrings__DefaultConnection = 'Data Source="' + $database + '"'

dotnet restore WITS.slnx
dotnet build WITS.slnx --no-restore
dotnet run --project tools/WITS.Migrator -- list-migrations
dotnet run --project tools/WITS.Migrator -- migrate
dotnet run --project tools/WITS.Seeder
dotnet run --project src/WITS.Api --no-launch-profile -- --urls http://localhost:5351