dotnet pack -c Release -o nupkg
dotnet tool install --configfile .\nupkg\nuget.config --add-source .\nupkg -g fsw