dotnet pack ./src/FsWorksheet.fsproj -c Release -o nupkg
dotnet tool install --add-source ./nupkg -g FSharp.Worksheet
