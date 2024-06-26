﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityPerformanceBenchmarkReporter.Entities;
using UnityPerformanceBenchmarkReporter.Report;

namespace UnityPerformanceBenchmarkReporter
{
    internal class Program
    {
        private static int indentLevel;

        private static readonly Dictionary<Type, string[]> ExcludedConfigFieldNames = new Dictionary<Type, string[]>
        {
            {typeof(EditorVersion), new []
            {
                "DateSeconds",
                "RevisionValue"
            }},
            {typeof(BuildSettings), new []
            {
                "DevelopmentPlayer"
            }},
            {typeof(PlayerSystemInfo), new []
            {
                "XrModel"
            }},
            {typeof(PlayerSettings), new []
            {
                "MtRendering", // Hidden because we have a calculated field, RenderThreadingMode, that provides a more succinct value (SingleThreaded, MultiThreaded, GfxJobs)
                "GraphicsJobs", // Hidden because we have a calculated field, RenderThreadingMode, that provides a more succinct value (SingleThreaded, MultiThreaded, GfxJobs)
                "VrSupported", // Hidden because this value doesn't seem to be coming through as 'True' when it should be true.
                "AndroidMinimumSdkVersion",
                "AndroidTargetSdkVersion"
            }}
        };

        private static int Main(string[] args)
        {
            var aggregateTestRunResults = new List<PerformanceTestRunResult>();
            var baselinePerformanceTestRunResults = new List<PerformanceTestRunResult>();
            var baselineTestResults = new List<TestResult>();
            var performanceTestRunResults = new List<PerformanceTestRunResult>();
            var testResults = new List<TestResult>();
            var performanceBenchmark = new PerformanceBenchmark(ExcludedConfigFieldNames);
            var optionsParser = new OptionsParser();

            optionsParser.ParseOptions(performanceBenchmark, args);

            IParser testResultParser = null;

            if (performanceBenchmark.FileType == ESupportedFileTypes.json)
            {
                testResultParser = new TestResultJsonParser();
            }
            else if (performanceBenchmark.FileType == ESupportedFileTypes.xml)
            {
                testResultParser = new TestResultXmlParser();
            }

            if (performanceBenchmark.BaselineResultFilesExist)
            {
                performanceBenchmark.AddBaselinePerformanceTestRunResults(testResultParser, baselinePerformanceTestRunResults, baselineTestResults);

                if (baselinePerformanceTestRunResults.Any())
                {
                    aggregateTestRunResults.AddRange(baselinePerformanceTestRunResults);
                }
                else
                {
                    Environment.Exit(1);
                }
            }

            if (performanceBenchmark.ResultFilesExist)
            {
                performanceBenchmark.AddPerformanceTestRunResults(testResultParser, performanceTestRunResults, testResults, baselineTestResults);

                if (performanceTestRunResults.Any())
                {
                    aggregateTestRunResults.AddRange(performanceTestRunResults);
                }
                else
                {
                    Environment.Exit(1);
                }
            }

            var performanceTestResults = new PerformanceTestRunResult[0];

            // If we have a baseline
            if (aggregateTestRunResults.Any(a => a.IsBaseline))
            {
                // Insert the baseline in the front of the array results; this way we can display the baseline first in the report
                Array.Resize(ref performanceTestResults, 1);
                performanceTestResults[0] = aggregateTestRunResults.First(a => a.IsBaseline);
            }

            var nonBaselineTestRunResults = aggregateTestRunResults.Where(a => !a.IsBaseline).ToList();

            nonBaselineTestRunResults.Sort((run1, run2) => string.Compare(run1.ResultName, run2.ResultName, StringComparison.Ordinal));


            foreach (var performanceTestRunResult in nonBaselineTestRunResults)
            {
                Array.Resize(ref performanceTestResults, performanceTestResults.Length + 1);
                performanceTestResults[performanceTestResults.Length - 1] = performanceTestRunResult;
            }

            performanceBenchmark.TestRunMetadataProcessor.PerformFinalMetadataUpdate(performanceBenchmark);

            var reportWriter = new ReportWriter(performanceBenchmark.TestRunMetadataProcessor);

            var path = reportWriter.WriteReport(
                performanceTestResults,
                performanceBenchmark.SigFig,
                performanceBenchmark.ReportDirPath,
                performanceBenchmark.BaselineResultFilesExist);
            WriteProgressedTestsAndMetricsToConsole(performanceTestResults, performanceBenchmark);
            WriteRegressedKnownTestsAndMetricsToConsole(performanceTestResults, performanceBenchmark);
            int result = WriteFailedTestsAndMetricsToConsole(performanceTestResults, performanceBenchmark);
            WriteLine($"Finished with Result {result}");

            if (performanceBenchmark.OpenReport)
            {
                // fire up the report in the browser, using shell execute
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }

            if (performanceBenchmark.ExportCSV)
            {
                ExportCSV(performanceTestResults, Path.GetDirectoryName(path) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(path) + ".csv");
            }
            
            return result;
        }

        private static int WriteFailedTestsAndMetricsToConsole(PerformanceTestRunResult[] performanceTestResults, PerformanceBenchmark performanceBenchmark)
        {
            var failedTestsExist = performanceTestResults.SelectMany(ptr => ptr.TestResults)
                .Any(tr => tr.State == (int)TestState.Failure);
            if (failedTestsExist)
            {
                WriteLine("FAILURE: One ore more performance test metric aggregations is out of threshold from the baseline value. REGRESSIONS!");
                WriteLine("-------------------------------------");
                WriteLine(" Performance tests with failed metrics");
                WriteLine("-------------------------------------");
                foreach (var performanceTestRunResult in performanceTestResults)
                {
                    var failedTests = performanceTestRunResult.TestResults.Where(tr => tr.State == (int)TestState.Failure);
                    if (failedTests.Any())
                    {
                        foreach (var failedTest in failedTests)
                        {
                            ++indentLevel;
                            WriteLine("{0}", failedTest.TestName);

                            var regressedSgs = failedTest.SampleGroupResults.Where(sgr => sgr.Regressed);
                            foreach (var sampleGroupResult in regressedSgs)
                            {
                                WriteLine("----");
                                WriteLine("Metric        : {0}", sampleGroupResult.SampleGroupName);
                                WriteLine("Aggregation   : {0}", sampleGroupResult.AggregationType);
                                WriteLine("Failed Value  : {0,8:F2}", sampleGroupResult.AggregatedValue);
                                WriteLine("Baseline Value: {0,8:F2}", sampleGroupResult.BaselineValue);
                                WriteLine("Threshold %   : {0,8:F2}", sampleGroupResult.Threshold);
                                WriteLine("Actual Diff % : {0,8:F2}", Math.Abs(sampleGroupResult.AggregatedValue - sampleGroupResult.BaselineValue) / sampleGroupResult.BaselineValue);
                            }
                            --indentLevel;
                            WriteLine("\r\n");
                        }
                    }
                }
            }
            return performanceBenchmark.FailOnBaseline && failedTestsExist ? 1 : 0;
        }

        private static void WriteRegressedKnownTestsAndMetricsToConsole(PerformanceTestRunResult[] performanceTestResults, PerformanceBenchmark performanceBenchmark)
        {
            bool loggedHeader = false;
            var passedTestsExist = performanceTestResults.SelectMany(ptr => ptr.TestResults)
                .Any(tr => tr.State == (int)TestState.Success);
            if (passedTestsExist)
            {

                foreach (var performanceTestRunResult in performanceTestResults)
                {
                    var passedTests = performanceTestRunResult.TestResults.Where(tr => tr.State == (int)TestState.Success);
                    if (passedTests.Any())
                    {
                        foreach (var tests in passedTests)
                        {
                            if (tests.SampleGroupResults.Any(sgr => sgr.RegressedKnown && sgr.Regressed))
                            {
                                if (!loggedHeader)
                                {
                                    loggedHeader = true;
                                    WriteLine("Info: One ore more performance test metric aggregations is out of threshold from the baseline value. KNOWN REGRESSIONS!");
                                    WriteLine("-------------------------------------");
                                    WriteLine(" Performance tests with Known Regressions metrics");
                                    WriteLine("-------------------------------------");
                                }

                                ++indentLevel;
                                WriteLine("{0}", tests.TestName);

                                var progressedSgs = tests.SampleGroupResults.Where(sgr => sgr.Progressed);
                                foreach (var sampleGroupResult in progressedSgs)
                                {
                                    WriteLine("----");
                                    WriteLine("Metric        : {0}", sampleGroupResult.SampleGroupName);
                                    WriteLine("Aggregation   : {0}", sampleGroupResult.AggregationType);
                                    WriteLine("New Value  : {0,8:F2}", sampleGroupResult.AggregatedValue);
                                    WriteLine("Baseline Value: {0,8:F2}", sampleGroupResult.BaselineValue);
                                    WriteLine("Threshold %   : {0,8:F2}", sampleGroupResult.Threshold);
                                    WriteLine("Actual Diff % : {0,8:F2}", Math.Abs(sampleGroupResult.BaselineValue - sampleGroupResult.AggregatedValue) / sampleGroupResult.BaselineValue);
                                    WriteLine($"Known Issue: {sampleGroupResult.KnownIssueDetails}");
                                }
                                --indentLevel;
                                WriteLine("\r\n");
                            }
                        }
                    }
                }
            }

        }

        private static void WriteProgressedTestsAndMetricsToConsole(PerformanceTestRunResult[] performanceTestResults, PerformanceBenchmark performanceBenchmark)
        {
            bool loggedHeader = false;
            var passedTestsExist = performanceTestResults.SelectMany(ptr => ptr.TestResults)
                .Any(tr => tr.State == (int)TestState.Success);
            if (passedTestsExist)
            {

                foreach (var performanceTestRunResult in performanceTestResults)
                {
                    var passedTests = performanceTestRunResult.TestResults.Where(tr => tr.State == (int)TestState.Success);
                    if (passedTests.Any())
                    {
                        foreach (var tests in passedTests)
                        {
                            if (tests.SampleGroupResults.Any(sgr => sgr.Progressed))
                            {
                                if (!loggedHeader)
                                {
                                    loggedHeader = true;
                                    WriteLine("Info: One ore more performance test metric aggregations is out of threshold from the baseline value. PROGRESSIONS!");
                                    WriteLine("-------------------------------------");
                                    WriteLine(" Performance tests with Progressed metrics");
                                    WriteLine("-------------------------------------");
                                }

                                ++indentLevel;
                                WriteLine("{0}", tests.TestName);

                                var progressedSgs = tests.SampleGroupResults.Where(sgr => sgr.Progressed);
                                foreach (var sampleGroupResult in progressedSgs)
                                {
                                    WriteLine("----");
                                    WriteLine("Metric        : {0}", sampleGroupResult.SampleGroupName);
                                    WriteLine("Aggregation   : {0}", sampleGroupResult.AggregationType);
                                    WriteLine("New Value  : {0,8:F2}", sampleGroupResult.AggregatedValue);
                                    WriteLine("Baseline Value: {0,8:F2}", sampleGroupResult.BaselineValue);
                                    WriteLine("Threshold %   : {0,8:F2}", sampleGroupResult.Threshold);
                                    WriteLine("Actual Diff % : {0,8:F2}", Math.Abs(sampleGroupResult.BaselineValue - sampleGroupResult.AggregatedValue) / sampleGroupResult.BaselineValue);
                                }
                                --indentLevel;
                                WriteLine("\r\n");
                            }
                        }
                    }
                }
            }

        }

        private static void WriteLine(string format, params object[] args)
        {
            Console.Write(new string('\t', indentLevel));
            Console.WriteLine(format, args);
        }
        
        private static void ExportCSV(PerformanceTestRunResult[] runResults, string path)
        {
            var sb = new StringBuilder();
            const char sep = ',';

            string header = string.Join(sep, new[]
            {
                "Test Name",
                "Run Name",
                "Version",
                "Sample Group Name",
                "Unit",
                "Increase Is Better",
                "Min",
                "Max",
                "Median",
                "Average",
                "Standard Deviation",
                "Sum",
            });
            
            sb.AppendLine(header);
            
            var byTestName = runResults
                .SelectMany(runResult => runResult.TestResults.Select(test => (runResult, test)))
                .GroupBy(x => x.test.TestName);
            
            foreach (var testGroup in byTestName)
            {
                foreach (var (runResult, test) in testGroup)
                {
                    foreach (var sampleGroup in test.SampleGroupResults)
                    {
                        sb.Append(EscapeCSV(test.TestName)).Append(sep);
                        sb.Append(EscapeCSV(runResult.ResultName)).Append(sep);
                        sb.Append(EscapeCSV(test.TestVersion)).Append(sep);
                        sb.Append(EscapeCSV(sampleGroup.SampleGroupName)).Append(sep);
                        sb.Append(EscapeCSV(sampleGroup.SampleUnit)).Append(sep);
                        sb.Append(sampleGroup.IncreaseIsBetter).Append(sep);
                        sb.Append(InvariantCulture(sampleGroup.Min)).Append(sep);
                        sb.Append(InvariantCulture(sampleGroup.Max)).Append(sep);
                        sb.Append(InvariantCulture(sampleGroup.Median)).Append(sep);
                        sb.Append(InvariantCulture(sampleGroup.Average)).Append(sep);
                        sb.Append(InvariantCulture(sampleGroup.StandardDeviation)).Append(sep);
                        sb.Append(InvariantCulture(sampleGroup.Sum)).Append(sep);
                        sb.AppendLine();
                    }
                }
            }
            
            File.WriteAllText(path, sb.ToString());

            string EscapeCSV(string str)
            {
                return str.Contains(sep) || str.Contains('"') || str.Contains('\n')
                    ? $"\"{str.Replace("\"", "\"\"")}\""
                    : str;
            }
            
            string InvariantCulture(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
