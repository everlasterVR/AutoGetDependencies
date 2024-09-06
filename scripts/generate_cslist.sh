#!/bin/bash

files=$(grep -o '<Compile Include="[^"]*"' CheckSceneDependencies.csproj | sed 's/<Compile Include="//; s/"//')
echo "$files" > CheckSceneDependencies.cslist
