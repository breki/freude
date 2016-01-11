using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Flubu;
using Flubu.Builds;
using Flubu.Builds.Tasks.AnalysisTasks;
using Flubu.Builds.Tasks.TestingTasks;
using Flubu.Targeting;

//css_ref Flubu.dll;
//css_ref Flubu.Contrib.dll;

namespace BuildScripts
{
    public class BuildScript
    {
        public static int Main(string[] args)
        {
            DefaultBuildScriptRunner runner = new DefaultBuildScriptRunner(ConstructTargets, ConfigureBuildProperties);
            return runner.Run(args);
        }

        private static void ConstructTargets(TargetTree targetTree)
        {
            targetTree.AddTarget("rebuild")
                .SetDescription("Builds the product, packages it, deploys and tests it on a local instance")
                .DependsOn("compile", "dupfinder", "tests").SetAsDefault();

            targetTree.GetTarget("fetch.build.version").Do(TargetFetchBuildVersion);
            
            targetTree.AddTarget("tests")
                .SetDescription("Runs tests on the project").Do(TargetRunTests).DependsOn("load.solution");
            
            targetTree.AddTarget("dupfinder")
                .SetDescription("Runs R# dupfinder to find code duplicates").Do(TargetDupFinder);
        }

        private static void ConfigureBuildProperties(TaskSession session)
        {
            session.Properties.Set(BuildProps.CompanyName, "Igor Brejc");
            session.Properties.Set(BuildProps.CompanyCopyright, "Copyright (C) 2015-2016 Igor Brejc");
            session.Properties.Set(BuildProps.ProductId, "Freude");
            session.Properties.Set(BuildProps.ProductName, "Freude");
            session.Properties.Set(BuildProps.SolutionFileName, "Freude.sln");
            session.Properties.Set(BuildProps.TargetDotNetVersion, FlubuEnvironment.Net40VersionNumber);
            session.Properties.Set(BuildProps.ToolsVersion, FlubuEnvironment.Net40VersionNumber);
            session.Properties.Set(BuildProps.VersionControlSystem, VersionControlSystem.Mercurial);
            session.Properties.Set(BuildProps.FxcopDir, "Microsoft Fxcop 10.0");
        }

        private static void TargetFetchBuildVersion (ITaskContext context)
        {
            Version version = BuildTargets.FetchBuildVersionFromFile (context);
            context.Properties.Set (BuildProps.BuildVersion, version);
            context.WriteInfo ("The build version will be {0}", version);
        }

        private static void TargetRunTests(ITaskContext context)
        {
            string config = context.Properties[BuildProps.BuildConfiguration];
            List<string> testAssemblies = new List<string> ();
            testAssemblies.Add (Path.Combine (string.Format(CultureInfo.InvariantCulture, "Freude.Tests/bin/{0}/Freude.Tests.dll", config)));

            NUnitWithDotCoverTask task = new NUnitWithDotCoverTask (
                @"packages\NUnit.Console.3.0.1\tools\nunit3-console.exe",
                testAssemblies);
            task.FailBuildOnViolations = false;
            task.NUnitCmdLineOptions = "--labels=All --trace=Verbose --verbose";
            task.DotCoverFilters = "-:module=*.Tests;-:class=*Contract;-:class=*Contract`*;-:module=LibroLib*;-:module=Syborg;";
            task.Execute (context);
        }

        private static void TargetDupFinder(ITaskContext context)
        {
            RunDupFinderAnalysisTask task = new RunDupFinderAnalysisTask();
            task.Execute(context);
        }
    }
}