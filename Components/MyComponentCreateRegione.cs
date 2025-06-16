using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace SharpShadow.Components
{
    public class CreateSsRegioneComponent : GH_Component
    {
        public CreateSsRegioneComponent()
            : base("CreateSsRegione", "CreateReg",
                   "Creates SsRegione objects from SsSilhouettes and SsSettings.",
                   "SharpShadow", "Region")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSettings", "S", "SsSettings object", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("RegionCurves", "RC", "SsRegione RegionCurves", GH_ParamAccess.tree);
            pManager.AddGenericParameter("SsRegiones", "SR", "SsRegione objects", GH_ParamAccess.list);
            pManager.AddTextParameter("LogMessages", "LM", "Step-by-step log messages and summary report", GH_ParamAccess.tree); // 更新描述
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize inputs
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            SsSettings settings = null;
            List<string> logMessages = new List<string> { "Starting CreateSsRegione" };
            List<string> failureReasons = new List<string>(); // 存储失败原因

            // Input validation
            if (!DA.GetDataList(0, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                failureReasons.Add("Failed to get SsSilhouettes.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
                return;
            }

            if (!DA.GetData(1, ref settings) || settings == null)
            {
                logMessages.Add("Error: SsSettings is null or invalid.");
                failureReasons.Add("SsSettings is null or invalid.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
                return;
            }

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                failureReasons.Add("No silhouettes provided.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
                return;
            }

            if (settings.Tolerance <= 0)
            {
                logMessages.Add("Error: Invalid tolerance in SsSettings.");
                failureReasons.Add("Invalid tolerance in SsSettings.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
                return;
            }

            logMessages.Add($"Inputs: {silhouettes.Count} silhouettes, Tolerance: {settings.Tolerance}, ViewName: {settings.ViewName ?? "null"}");

            // Get camera plane
            if (!settings.GetCameraPlane(out Plane testPlane, out string planeLog))
            {
                logMessages.Add($"Error: {planeLog}");
                failureReasons.Add(planeLog);
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
                return;
            }
            logMessages.Add("Camera plane acquired successfully.");

            // Generate SsRegione objects
            SsRegione.ResetNumber();
            List<SsRegione> regions = null;
            List<Curve> regionCurves = new List<Curve>();
            int failedRegionCount = 0;

            try
            {
                regions = SsRegione.CreateRegionsFromSilhouettes(silhouettes, testPlane, settings.Tolerance, out var regionLogs);
                logMessages.AddRange(regionLogs.Where(l => l.Contains("Error") || l.Contains("Warning"))); // 仅记录错误和警告
                if (regions == null || regions.Count == 0)
                {
                    logMessages.Add("Error: No regions created.");
                    failureReasons.Add("No regions created from silhouettes.");
                }
                else
                {
                    logMessages.Add($"Created {regions.Count} regions.");
                }

                if (regions != null)
                {
                    // Compute hit geometries for each region
                    foreach (var region in regions)
                    {
                        if (region == null || region.RegionCurve == null)
                        {
                            logMessages.Add($"Warning: Region {region?.RegionCode ?? -1} is null or has invalid RegionCurve.");
                            failureReasons.Add($"Region {region?.RegionCode ?? -1}: Null or invalid RegionCurve.");
                            regionCurves.Add(null);
                            failedRegionCount++;
                            continue;
                        }

                        // Compute HittedGeobyGuid
                        try
                        {
                            region.ComputeHitGeometries(silhouettes, settings, testPlane.Normal, settings.Tolerance, out var hitLogs);
                            logMessages.AddRange(hitLogs.Where(l => l.Contains("Error") || l.Contains("Warning"))); // 仅记录错误和警告
                            regionCurves.Add(region.RegionCurve);
                        }
                        catch (Exception ex)
                        {
                            logMessages.Add($"Error: Region {region.RegionCode}: Failed to compute hit geometries: {ex.Message}");
                            failureReasons.Add($"Region {region.RegionCode}: Failed to compute hit geometries: {ex.Message}");
                            regionCurves.Add(region.RegionCurve);
                            failedRegionCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error: Exception in region creation: {ex.Message}");
                failureReasons.Add($"Exception in region creation: {ex.Message}");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
                return;
            }

            // Generate Summary Report
            List<string> summaryReport = new List<string>
            {
                $"Total Silhouettes Input: {silhouettes.Count}",
                $"Regions Created: {regions?.Count ?? 0}",
                $"Failed Regions or Hit Computations: {failedRegionCount}",
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

            // Set outputs
            try
            {
                var regionTree = new Grasshopper.DataTree<Curve>();
                for (int i = 0; i < regionCurves.Count; i++)
                {
                    regionTree.Add(regionCurves[i], new GH_Path(i));
                }
                DA.SetDataTree(0, regionTree);
                DA.SetDataList(1, regions);

                // Output LogMessages and SummaryReport to different paths
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0)); // Step-by-step logs
                logTree.AppendRange(summaryReport.Select(s => new GH_String(s)), new GH_Path(0, 1)); // Summary report
                DA.SetDataTree(2, logTree);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error: Setting outputs failed: {ex.Message}");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(2, logTree);
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid
        {
            get { return new Guid("C76D8ABE-3EA4-40C2-90F9-56B4799308D5"); }
        }
    }
}