#!/bin/bash

NGD=./.nuget
NGP=$NGD/NuGet.exe

echo -n "Checking for nuget... "
if [ ! -f $NGP  ]; then
	if [ !  -d $NGD  ]; then
		mkdir $NGD
	fi
	echo -n "\nDownloading Nuget... "
	wget -O $NGP http://nuget.org/nuget.exe
	echo "done"

else
	echo "found"
fi

if [ ! -x $NGP ]; then
	chmod +x $NGP
fi

FT=./build/tools/FAKE/tools/FAKE.exe

if [ ! -f $FT ]; then
	$NGP install FAKE -OutputDirectory build/tools -ExcludeVersion
fi

if [ ! -x $FT ]; then
	chmod +x $FT
fi

export TARGET=BUILD

$FT build/build.fsx target=$TARGET

