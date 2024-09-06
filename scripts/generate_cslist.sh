#!/bin/bash

files=$(grep -o '<Compile Include="[^"]*"' AutoGetDependencies.csproj | sed 's/<Compile Include="//; s/"//')
echo "$files" > AutoGetDependencies.cslist
