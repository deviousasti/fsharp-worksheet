dotnet pack .\src\FsWorksheet.fsproj -c Release -o nupkg
dotnet tool install --configfile .\nupkg\nuget.config --add-source .\nupkg -g fsw