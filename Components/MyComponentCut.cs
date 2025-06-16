using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace SharpShadow.Components
{
    public class MyComponentCut : GH_Component
    {
        public MyComponentCut()
            : base("CutGeometry", "CutGeo",
                   "Cuts GeometryReference using SilCrv and classifies parts.",
                   "SharpShadow", "Silhouette")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSettings", "Set", "SsSettings object containing tolerance", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("JeongParts", "J", "Jeong parts (light parts) from cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("JamParts", "M", "Jam parts (shadow parts) from cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("JingParts", "Ji", "Jing parts from advanced cutting (currently empty)", GH_ParamAccess.list);
            pManager.AddBooleanParameter("IsFailed", "JF", "List of IsFailedCutter flags indicating failed cuts", GH_ParamAccess.list);
            pManager.AddGenericParameter("UpdatedSsSilhouettes", "US", "SsSilhouettes with updated parts", GH_ParamAccess.list);
            pManager.AddTextParameter("LogMessages", "LM", "Step-by-step log messages and summary report", GH_ParamAccess.tree); // 更新描述
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize inputs
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            SsSettings settings = null;
            List<string> logMessages = new List<string> { "Starting CutGeometry" };
            List<string> failureReasons = new List<string>(); // 存储失败原因

            if (!DA.GetDataList(0, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(5, logTree);
                return;
            }

            if (!DA.GetData(1, ref settings) || settings == null)
            {
                logMessages.Add("Error: Failed to get SsSettings or settings is null.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(5, logTree);
                return;
            }

            logMessages.Add($"Input: {silhouettes.Count} silhouettes, Tolerance: {settings.Tolerance}");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(5, logTree);
                return;
            }

            // Initialize output collections
            List<GeometryBase> jeongParts = new List<GeometryBase>();
            List<GeometryBase> jamParts = new List<GeometryBase>();
            List<GeometryBase> jingParts = new List<GeometryBase>();
            List<bool> isFailedCutters = new List<bool>();

            double tolerance = settings.Tolerance;

            // Step 1: Simple Cutting for all silhouettes
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null)
                {
                    logMessages.Add("Error: Null silhouette, skipping.");
                    failureReasons.Add("Null silhouette detected.");
                    isFailedCutters.Add(false);
                    continue;
                }

                if (silhouette.GeometryReference == null || !silhouette.GeometryReference.IsValid)
                {
                    logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Invalid or null GeometryReference.");
                    failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: Invalid or null GeometryReference.");
                    silhouette.IsFailedCutter = true;
                    isFailedCutters.Add(true);
                    continue;
                }

                if (silhouette.SilCrv == null || !silhouette.SilCrv.IsValid)
                {
                    logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Invalid or null SilCrv.");
                    failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: Invalid or null SilCrv.");
                    silhouette.IsFailedCutter = true;
                    isFailedCutters.Add(true);
                    continue;
                }

                try
                {
                    // Simple Cutting
                    GeometryBase[] simpleParts = silhouette.DivideChiaroSicuro(tolerance, logMessages);
                    if (simpleParts != null && simpleParts.Length >= 2)
                    {
                        // Classify based on number of parts
                        if (simpleParts.Length == 2)
                        {
                            silhouette.ClassifyJeong2JamParts(simpleParts, tolerance, logMessages);
                        }
                        else
                        {
                            silhouette.ClassifyJeongMultiJamParts(simpleParts, tolerance, logMessages, out _, out _, out _);
                        }
                        silhouette.IsFailedCutter = false;
                    }
                    else
                    {
                        logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Simple cutting failed to produce enough parts.");
                        failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: Simple cutting failed to produce enough parts.");
                        silhouette.IsFailedCutter = true;
                    }

                    isFailedCutters.Add(silhouette.IsFailedCutter);
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Exception in simple cutting: {ex.Message}");
                    failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: Exception in simple cutting: {ex.Message}");
                    silhouette.IsFailedCutter = true;
                    isFailedCutters.Add(true);
                }
            }

            // Step 2: Advanced Cutting for failed cases
            foreach (var silhouette in silhouettes.Where(s => s != null && s.IsFailedCutter))
            {
                try
                {
                    GeometryBase[] advancedParts = silhouette.DivideChiaroSicuroAdvanced(tolerance, logMessages);
                    if (advancedParts != null && advancedParts.Length >= 2)
                    {
                        // Classify based on number of parts
                        if (advancedParts.Length == 2)
                        {
                            silhouette.ClassifyJeong2JamParts(advancedParts, tolerance, logMessages);
                        }
                        else
                        {
                            silhouette.ClassifyJeongMultiJamParts(advancedParts, tolerance, logMessages, out _, out _, out _);
                        }
                        silhouette.IsFailedCutter = false;
                    }
                    else
                    {
                        logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Advanced cutting failed to produce enough parts.");
                        failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: Advanced cutting failed to produce enough parts.");
                        silhouette.IsFailedCutter = true;
                    }

                    // Update IsFailedCutter in the list
                    int idx = silhouettes.IndexOf(silhouette);
                    isFailedCutters[idx] = silhouette.IsFailedCutter;
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Exception in advanced cutting: {ex.Message}");
                    failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: Exception in advanced cutting: {ex.Message}");
                    silhouette.IsFailedCutter = true;
                    int idx = silhouettes.IndexOf(silhouette);
                    isFailedCutters[idx] = true;
                }
            }

            // Step 3: Collect outputs
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null)
                {
                    jeongParts.Add(null);
                    jamParts.Add(null);
                    jingParts.AddRange(new List<GeometryBase>());
                    continue;
                }

                // Jeong Parts
                if (silhouette.JeongParts != null && silhouette.JeongParts.Any())
                {
                    jeongParts.AddRange(silhouette.JeongParts);
                }
                else if (silhouette.IsFailedCutter)
                {
                    jeongParts.Add(silhouette.GeometryReference);
                }
                else
                {
                    jeongParts.Add(null);
                }

                // Jam Parts
                if (silhouette.JamParts != null && silhouette.JamParts.Any())
                {
                    jamParts.AddRange(silhouette.JamParts);
                }
                else
                {
                    jamParts.Add(null);
                }

                // Jing Parts
                jingParts.AddRange(silhouette.JingParts ?? new List<GeometryBase>());
            }

            // Step 4: Generate Summary Report
            List<string> summaryReport = new List<string>
            {
                $"Total Silhouettes Processed: {silhouettes.Count}",
                $"Failed Cuts: {isFailedCutters.Count(f => f)}",
                "Failure Reasons:"
            };
            if (failureReasons.Any())
            {
                summaryReport.AddRange(failureReasons);
            }
            else
            {
                summaryReport.Add("No failures recorded.");
            }

            // Step 5: Set outputs with paths
            try
            {
                DA.SetDataList(0, jeongParts);
                DA.SetDataList(1, jamParts);
                DA.SetDataList(2, jingParts);
                DA.SetDataList(3, isFailedCutters);
                DA.SetDataList(4, silhouettes);

                // Output LogMessages and SummaryReport to different paths
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0)); // Step-by-step logs
                logTree.AppendRange(summaryReport.Select(s => new GH_String(s)), new GH_Path(0, 1)); // Summary report
                DA.SetDataTree(5, logTree);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(5, logTree);
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid
        {
            get { return new Guid("4C267BE0-64F0-4170-B3BE-393F4EF38D3A"); }
        }
    }
}