// Contains .NET 6-related Cake targets

var ext = IsRunningOnWindows() ? ".exe" : "";
var dotnetPath = $"./bin/dotnet/dotnet{ext}";

// Tasks for CI

Task("dotnet")
    .Description("Provisions .NET 6 into bin/dotnet based on eng/Versions.props")
    .Does(() =>
    {
        if (!localDotnet) 
            return;

        DotNetCoreBuild("./src/DotNet/DotNet.csproj", new DotNetCoreBuildSettings
        {
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .EnableBinaryLogger($"{logDirectory}/dotnet-{configuration}.binlog")
                .SetConfiguration(configuration),
        });
    });

Task("dotnet-local-workloads")
    .Does(() =>
    {
        if (!localDotnet) 
            return;
        
        DotNetCoreBuild("./src/DotNet/DotNet.csproj", new DotNetCoreBuildSettings
        {
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .EnableBinaryLogger($"{logDirectory}/dotnet-{configuration}.binlog")
                .SetConfiguration(configuration)
                .WithProperty("InstallWorkloadPacks", "false"),
        });

        DotNetCoreBuild("./src/DotNet/DotNet.csproj", new DotNetCoreBuildSettings
        {
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .EnableBinaryLogger($"{logDirectory}/dotnet-install-{configuration}.binlog")
                .SetConfiguration(configuration)
                .WithTarget("Install"),
            ToolPath = dotnetPath,
        });
    });

Task("dotnet-buildtasks")
    .IsDependentOn("dotnet")
    .Does(() =>
    {
        RunMSBuildWithDotNet("./Microsoft.Maui.BuildTasks.slnf");
    });

Task("dotnet-build")
    .IsDependentOn("dotnet")
    .Description("Build the solutions")
    .Does(() =>
    {
        RunMSBuildWithDotNet("./Microsoft.Maui.BuildTasks.slnf");
        if (IsRunningOnWindows())
            RunMSBuildWithDotNet("./Microsoft.Maui.sln");
        else
            RunMSBuildWithDotNet("./Microsoft.Maui-mac.slnf");
    });

Task("dotnet-samples")
    .Does(() =>
    {
        RunMSBuildWithDotNet("./Microsoft.Maui.Samples.slnf", new Dictionary<string, string> {
            ["UseWorkload"] = "true",
            // ["GenerateAppxPackageOnBuild"] = "true",
        });
    });

Task("dotnet-templates")
    .Does(() =>
    {
        if (localDotnet)
            SetDotNetEnvironmentVariables();

        var dn = localDotnet ? dotnetPath : "dotnet";

        var templatesTest = tempDirectory.Combine("templatesTest");

        EnsureDirectoryExists(templatesTest);
        CleanDirectories(templatesTest.FullPath);

        // Create empty Directory.Build.props/targets
        FileWriteText(templatesTest.CombineWithFilePath("Directory.Build.props"), "<Project/>");
        FileWriteText(templatesTest.CombineWithFilePath("Directory.Build.targets"), "<Project/>");
        CopyFileToDirectory(File("./NuGet.config"), templatesTest);

        // See: https://github.com/dotnet/project-system/blob/main/docs/design-time-builds.md
        var designTime = new Dictionary<string, string> {
            { "DesignTimeBuild", "true" },
            { "BuildingInsideVisualStudio", "true" },
            { "SkipCompilerExecution", "true" },
            // NOTE: this overrides a default setting that supports VS Mac
            // See: https://github.com/xamarin/xamarin-android/blob/94c2a3d86a2e0e74863b57e3c5c61dbd29daa9ea/src/Xamarin.Android.Build.Tasks/Xamarin.Android.Common.props.in#L19
            { "AndroidUseManagedDesignTimeResourceGenerator", "true" },
        };

        var properties = new Dictionary<string, string> {
            // Properties that ensure we don't use cached packages, and *only* the empty NuGet.config
            { "RestoreNoCache", "true" },
            // { "GenerateAppxPackageOnBuild", "true" },
            { "RestorePackagesPath", MakeAbsolute(templatesTest.CombineWithFilePath("packages")).FullPath },
            { "RestoreConfigFile", MakeAbsolute(templatesTest.CombineWithFilePath("nuget.config")).FullPath },

            // Avoid iOS build warning as error on Windows: There is no available connection to the Mac. Task 'VerifyXcodeVersion' will not be executed
            { "CustomBeforeMicrosoftCSharpTargets", MakeAbsolute(File("./src/Templates/TemplateTestExtraTargets.targets")).FullPath },
        };

        var templates = new Dictionary<string, Action<DirectoryPath>> {
            { "maui:maui", null },
            { "mauiblazor:maui-blazor", null },
            { "mauilib:mauilib", null },
            { "mauicorelib:mauilib", dir => {
                CleanDirectories(dir.Combine("Platforms").FullPath);
                ReplaceTextInFiles($"{dir}/*.csproj", "UseMaui", "UseMauiCore");
                ReplaceTextInFiles($"{dir}/*.csproj", "SingleProject", "EnablePreviewMsixTooling");
            } },
        };

        var alsoPack = new [] {
            "mauilib"
        };

        foreach (var template in templates)
        {
            foreach (var forceDotNetBuild in new [] { true, false })
            {
                // macOS does not support msbuild
                if (!IsRunningOnWindows() && !forceDotNetBuild)
                    continue;

                var type = forceDotNetBuild ? "DotNet" : "MSBuild";
                var projectName = template.Key.Split(":")[0];
                var templateName = template.Key.Split(":")[1];

                projectName = $"{templatesTest}/{projectName}_{type}";

                // Create
                StartProcess(dn, $"new {templateName} -o \"{projectName}\"");

                // Modify
                if (template.Value != null)
                    template.Value(projectName);

                // Enable Tizen
                ReplaceTextInFiles($"{projectName}/*.csproj",
                    "<!-- <TargetFrameworks>$(TargetFrameworks);net6.0-tizen</TargetFrameworks> -->",
                    "<TargetFrameworks>$(TargetFrameworks);net6.0-tizen</TargetFrameworks>");

                // Build
                RunMSBuildWithDotNet(projectName, properties, warningsAsError: true, forceDotNetBuild: forceDotNetBuild);

                // Pack
                if (alsoPack.Contains(templateName)) {
                    var packProperties = new Dictionary<string, string>(properties);
                    packProperties["PackageVersion"] = FileReadText("GitInfo.txt").Trim();
                    RunMSBuildWithDotNet(projectName, packProperties, warningsAsError: true, forceDotNetBuild: forceDotNetBuild, target: "Pack");
                }
            }
        }

        try
        {
            CleanDirectories(templatesTest.FullPath);
        }
        catch
        {
            Information("Unable to clean up templates directory.");
        }
    });

Task("dotnet-test")
    .IsDependentOn("dotnet")
    .Description("Build the solutions")
    .Does(() =>
    {
        var tests = new []
        {
            "**/Controls.Core.UnitTests.csproj",
            "**/Controls.Xaml.UnitTests.csproj",
            "**/Core.UnitTests.csproj",
            "**/Essentials.UnitTests.csproj",
            "**/Resizetizer.UnitTests.csproj",
        };

        var success = true;

        foreach (var test in tests)
        {
            foreach (var project in GetFiles(test))
            {
                try
                {
                    RunTestWithLocalDotNet(project.FullPath);
                }
                catch
                {
                    success = false;
                }
            }
        }

        if (!success)
            throw new Exception("Some tests failed. Check the logs or test results.");
    });

Task("dotnet-pack-maui")
    .Does(() =>
    {
        DotNetCoreTool("pwsh", new DotNetCoreToolSettings
        {
            DiagnosticOutput = true,
            ArgumentCustomization = args => args.Append($"-NoProfile ./eng/package.ps1 -configuration \"{configuration}\"")
        });
    });

Task("dotnet-pack-additional")
    .Does(() =>
    {
        // Download some additional symbols that need to be archived along with the maui symbols:
        //  - _NativeAssets.windows
        //     - libSkiaSharp.pdb
        //     - libHarfBuzzSharp.pdb
        var assetsDir = $"./artifacts/additional-assets";
        var nativeAssetsVersion = XmlPeek("./eng/Versions.props", "/Project/PropertyGroup/_SkiaSharpNativeAssetsVersion");
        NuGetInstall("_NativeAssets.windows", new NuGetInstallSettings
        {
            Version = nativeAssetsVersion,
            ExcludeVersion = true,
            OutputDirectory = assetsDir,
            Source = new[] { "https://aka.ms/skiasharp-eap/index.json" },
        });
        foreach (var nupkg in GetFiles($"{assetsDir}/**/*.nupkg"))
            DeleteFile(nupkg);
        Zip(assetsDir, $"{assetsDir}.zip");
    });

Task("dotnet-pack-library-packs")
    .Does(() =>
    {
        var tempDir = $"./artifacts/library-packs-temp";

        var destDir = $"./artifacts/library-packs";
        EnsureDirectoryExists(destDir);
        CleanDirectories(destDir);

        void Download(string id, string version, params string[] sources)
        {
            version = XmlPeek("./eng/Versions.props", "/Project/PropertyGroup/" + version);

            NuGetInstall(id, new NuGetInstallSettings
            {
                Version = version,
                ExcludeVersion = false,
                OutputDirectory = tempDir,
                Source = sources,
            });

            CopyFiles($"{tempDir}/**/" + id + "." + version + ".nupkg", destDir, false);
            CleanDirectories(tempDir);
        }

        Download("Microsoft.Maui.Graphics", "MicrosoftMauiGraphicsVersion", "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet6/nuget/v3/index.json");
        Download("Microsoft.Maui.Graphics.Win2D.WinUI.Desktop", "MicrosoftMauiGraphicsVersion", "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet6/nuget/v3/index.json", "https://api.nuget.org/v3/index.json");
    });

Task("dotnet-pack")
    .IsDependentOn("dotnet-pack-maui")
    .IsDependentOn("dotnet-pack-additional")
    .IsDependentOn("dotnet-pack-library-packs");

Task("dotnet-build-test")
    .IsDependentOn("dotnet")
    .IsDependentOn("dotnet-buildtasks")
    .IsDependentOn("dotnet-build")
    .IsDependentOn("dotnet-test");

Task("dotnet-diff")
    .Does(() =>
    {
        var nupkgs = GetFiles($"./artifacts/**/*.nupkg");
        if (!nupkgs.Any())
        {
            Warning($"##vso[task.logissue type=warning]No NuGet packages were found.");
        }
        else
        {
            // clean all working folders
            var diffCacheDir = tempDirectory.Combine("diffCache");
            EnsureDirectoryExists(diffCacheDir);
            CleanDirectories(diffCacheDir.FullPath);
            EnsureDirectoryExists(diffDirectory);
            CleanDirectories(diffDirectory.FullPath);

            // run the diff
            foreach (var nupkg in nupkgs)
            {
                DotNetCoreTool("api-tools", new DotNetCoreToolSettings
                {
                    DiagnosticOutput = true,
                    ArgumentCustomization = builder => builder
                        .Append("nuget-diff")
                        .AppendQuoted(nupkg.FullPath)
                        .Append("--latest")
                        // .Append("--verbose")
                        .Append("--prerelease")
                        .Append("--group-ids")
                        .Append("--ignore-unchanged")
                        .AppendSwitchQuoted("--output", diffDirectory.FullPath)
                        .AppendSwitchQuoted("--cache", diffCacheDir.FullPath)
                });
            }

            // clean working folders
            try
            {
                CleanDirectories(diffCacheDir.FullPath);
            }
            catch
            {
                Information("Unable to clean up diff cache directory.");
            }

            var diffs = GetFiles($"{diffDirectory}/**/*.md");
            if (!diffs.Any())
            {
                Warning($"##vso[task.logissue type=warning]No NuGet diffs were found.");
            }
            else
            {
                // clean working folders
                var temp = diffCacheDir.Combine("md-files");
                EnsureDirectoryExists(diffCacheDir);
                CleanDirectories(diffCacheDir.FullPath);

                // copy and rename files for UI
                foreach (var diff in diffs)
                {
                    var segments = diff.Segments.Reverse().ToArray();
                    var nugetId = segments[2];
                    var platform = segments[1];
                    var assembly = ((FilePath)segments[0]).GetFilenameWithoutExtension().GetFilenameWithoutExtension();
                    var breaking = segments[0].EndsWith(".breaking.md");

                    // using non-breaking spaces
                    var newName = breaking ? "[BREAKING]   " : "";
                    newName += $"{nugetId}    {assembly} ({platform}).md";
                    var newPath = diffCacheDir.CombineWithFilePath(newName);

                    CopyFile(diff, newPath);
                }

                // push changes to UI
                var temps = GetFiles($"{diffCacheDir}/**/*.md");
                foreach (var t in temps.OrderBy(x => x.FullPath))
                {
                    Information($"##vso[task.uploadsummary]{t}");
                }
            }
        }
    });

// Tasks for Local Development

Task("VS-DOGFOOD")
    .Description("Provisions .NET 6 and launches an instance of Visual Studio using it.")
    .IsDependentOn("dotnet")
    .Does(() =>
    {
        StartVisualStudioForDotNet6(null);
    });

Task("VS-NET6")
    .Description("Provisions .NET 6 and launches an instance of Visual Studio using it.")
    .IsDependentOn("Clean")
    .IsDependentOn("dotnet")
    .IsDependentOn("dotnet-buildtasks")
    .Does(() =>
    {
        StartVisualStudioForDotNet6();
    });

Task("VS-WINUI")
    .Description("Provisions .NET 6 and launches an instance of Visual Studio with WinUI projects.")
        .IsDependentOn("VS-NET6");
    //  .IsDependentOn("dotnet") WINUI currently can't launch application with local dotnet
    //  .IsDependentOn("dotnet-buildtasks")

Task("VS-ANDROID")
    .Description("Provisions .NET 6 and launches an instance of Visual Studio with Android projects.")
    .IsDependentOn("Clean")
    .IsDependentOn("dotnet")
    .IsDependentOn("dotnet-buildtasks")
    .Does(() =>
    {
        DotNetCoreRestore("./Microsoft.Maui.sln", new DotNetCoreRestoreSettings
        {
            ToolPath = dotnetPath
        });

        // VS has trouble building all the references correctly so this makes sure everything is built
        // and we're ready to go right when VS launches
        RunMSBuildWithDotNet("./src/Controls/samples/Controls.Sample/Maui.Controls.Sample.csproj");
        StartVisualStudioForDotNet6("./Microsoft.Maui.Droid.sln");
    });

string FindMSBuild()
{
    if (!string.IsNullOrWhiteSpace(MSBuildExe))
        return MSBuildExe;

    if (IsRunningOnWindows())
    {
        var vsInstallation = VSWhereLatest(new VSWhereLatestSettings { Requires = "Microsoft.Component.MSBuild", IncludePrerelease = true });
        if (vsInstallation != null)
        {
            var path = vsInstallation.CombineWithFilePath(@"MSBuild\Current\Bin\MSBuild.exe");
            if (FileExists(path))
                return path.FullPath;

            path = vsInstallation.CombineWithFilePath(@"MSBuild\15.0\Bin\MSBuild.exe");
            if (FileExists(path))
                return path.FullPath;
        }
    }
    return "msbuild";
}

void SetDotNetEnvironmentVariables()
{
    var dotnet = MakeAbsolute(Directory("./bin/dotnet/")).ToString();

    SetEnvironmentVariable("DOTNET_INSTALL_DIR", dotnet);
    SetEnvironmentVariable("DOTNET_ROOT", dotnet);
    SetEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", dotnet);
    SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
    SetEnvironmentVariable("MSBuildEnableWorkloadResolver", "true");
    SetEnvironmentVariable("PATH", dotnet, prepend: true);
}

void StartVisualStudioForDotNet6(string sln = null)
{
    if (sln == null)
    {
        if (IsRunningOnWindows())
        {
            sln = "./Microsoft.Maui.sln";
        }
        else
        {
            sln = "./Microsoft.Maui-mac.slnf";
        }
    }
    if (isCIBuild)
    {
        Information("This target should not run on CI.");
        return;
    }
    if(localDotnet)
    {
        SetDotNetEnvironmentVariables();
        SetEnvironmentVariable("_ExcludeMauiProjectCapability", "true");
    }
    if (IsRunningOnWindows())
    {
        bool includePrerelease = true;

        if (!String.IsNullOrEmpty(vsVersion))
            includePrerelease = (vsVersion == "preview");

        var vsLatest = VSWhereLatest(new VSWhereLatestSettings { IncludePrerelease = includePrerelease, });
        if (vsLatest == null)
            throw new Exception("Unable to find Visual Studio!");
       
        StartProcess(vsLatest.CombineWithFilePath("./Common7/IDE/devenv.exe"), sln);
    }
    else
    {
        StartProcess("open", new ProcessSettings{ Arguments = sln });
    }
}

// NOTE: These methods work as long as the "dotnet" target has already run

void RunMSBuildWithDotNet(
    string sln,
    Dictionary<string, string> properties = null,
    string target = "Build",
    bool warningsAsError = false,
    bool restore = true,
    string targetFramework = null,
    bool forceDotNetBuild = false)
{
    var useDotNetBuild = forceDotNetBuild || !IsRunningOnWindows() || target == "Run";

    var name = System.IO.Path.GetFileNameWithoutExtension(sln);
    var type = useDotNetBuild ? "dotnet" : "msbuild";
    var binlog = string.IsNullOrEmpty(targetFramework) ?
        $"\"{logDirectory}/{name}-{configuration}-{target}-{type}.binlog\"" :
        $"\"{logDirectory}/{name}-{configuration}-{target}-{targetFramework}-{type}.binlog\"";
    
    if(localDotnet)
        SetDotNetEnvironmentVariables();

    // If we're not on Windows, use ./bin/dotnet/dotnet
    if (useDotNetBuild)
    {
        var msbuildSettings = new DotNetCoreMSBuildSettings()
            .SetConfiguration(configuration)
            .SetMaxCpuCount(0)
            .WithTarget(target)
            .EnableBinaryLogger(binlog);

        if (warningsAsError)
        {
            msbuildSettings.TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Error);
        }

        if (properties != null)
        {
            foreach (var property in properties)
            {
                msbuildSettings.WithProperty(property.Key, property.Value);
            }
        }

        var dotnetBuildSettings = new DotNetCoreBuildSettings
        {
            MSBuildSettings = msbuildSettings,
        };
        dotnetBuildSettings.ArgumentCustomization = args =>
        {
            if (!restore)
                args.Append("--no-restore");

            if (!string.IsNullOrEmpty(targetFramework))
                args.Append($"-f {targetFramework}");

            return args;
        };

        if (localDotnet)
            dotnetBuildSettings.ToolPath = dotnetPath;

        DotNetCoreBuild(sln, dotnetBuildSettings);
    }
    else
    {
        // Otherwise we need to run MSBuild for WinUI support
        var msbuild = FindMSBuild();
        Information("Using MSBuild: {0}", msbuild);
        var msbuildSettings = new MSBuildSettings { ToolPath = msbuild }
            .SetConfiguration(configuration)
            .SetMaxCpuCount(0)
            .WithTarget(target)
            .EnableBinaryLogger(binlog);

        if (warningsAsError)
        {
            msbuildSettings.WarningsAsError = true;
        }
        if (restore)
        {
            msbuildSettings.WithRestore();
        }
        if (!string.IsNullOrEmpty(targetFramework))
        {
            msbuildSettings.WithProperty("TargetFramework", targetFramework);
        }

        if (properties != null)
        {
            foreach (var property in properties)
            {
                msbuildSettings.WithProperty(property.Key, property.Value);
            }
        }

        MSBuild(sln, msbuildSettings);
    }
}

void RunTestWithLocalDotNet(string csproj)
{
    var name = System.IO.Path.GetFileNameWithoutExtension(csproj);
    var binlog = $"{logDirectory}/{name}-{configuration}.binlog";
    var results = $"{name}-{configuration}.trx";

    if(localDotnet)
        SetDotNetEnvironmentVariables();

    DotNetCoreTest(csproj,
        new DotNetCoreTestSettings
        {
            Configuration = configuration,
            ToolPath = dotnetPath,
            NoBuild = true,
            Logger = $"trx;LogFileName={results}",
            ResultsDirectory = testResultsDirectory,
            ArgumentCustomization = args => args.Append($"-bl:{binlog}")
        });
}
