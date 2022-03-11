using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using UnityPerformanceBenchmarkReporter.Entities;
using UnityPerformanceBenchmarkReporter.Entities.New;

namespace UnityPerformanceBenchmarkReporter
{
    public class TestResultJsonParser : IParser
    {
        public PerformanceTestRun Parse(string path)
        {
            string report = "";
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    string stream = reader.ReadToEnd().Trim();

                    // json wrawrapped in invalid [], removed for valid json format 
                    if (stream[0] == '[' && stream[stream.Length - 1] == ']')
                        report = stream.Substring(1, stream.Length - 2);
                    else
                        report = stream;
                }
            }
            catch (System.Exception)
            {

                throw;
            }

            return ParseJson(report);
        }

        private static PerformanceTestRun ParseJson(string json)
        {

            PerformanceTestRun result;
            try
            {
                result = JsonConvert.DeserializeObject<PerformanceTestRun>(json);
            }
            catch (System.Exception)
            {

                throw;
            }


            return result;
        }


    }
}
