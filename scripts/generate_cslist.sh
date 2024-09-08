#!/bin/bash

is_packaging=false
if [ "$1" == "true" ]; then
  is_packaging=true
fi

files=$(grep -o '<Compile Include="[^"]*"' AutoGetDependencies.csproj | sed 's/<Compile Include="//; s/"//')

if $is_packaging; then
  files=$(echo "$files" | grep -v 'DevUtils.cs')
fi

echo "$files" > AutoGetDependencies.cslist
