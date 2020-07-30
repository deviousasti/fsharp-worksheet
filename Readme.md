# F# Worksheet

This is a tool for interacting with F# code like a spreadsheet.
Your code is divided into *cells*, and when a cell is changed it's dependents are updated.
No special coding conventions or additional libraries are required.

The central idea is that the nature of functional programs should allow them to be modeled as an acyclic graph - acyclic because forward references are not possible. 

## Usage

To install as a `dotnet` global tool, simply clone this repository and run `install.ps1`

To run pass the name of a script file to the application.

```
fsw program.fsx
```

To exit the application hit `return`.

## Demo

![fswatch2](https://user-images.githubusercontent.com/2375486/88964607-43709d00-d2c7-11ea-8f38-7d77e09e0d55.gif)

In this example, I change the definition of `toList` , you can see `toList tree` is evaluated. When I change the definition of `tree` , all the functions which use `tree` are evaluated.

