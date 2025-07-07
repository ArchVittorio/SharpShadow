using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace SharpShadow.Components
{
    public class ProjecteeSelect : GH_Component
    {
        public ProjecteeSelect()
            : base("ProjecteeSelect", "ProjSel",
                   "Selects SsSilhouette objects based on matching GeometryBase Guids.",
                   "SharpShadow", "Silhouette")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects", GH_ParamAccess.list);
            pManager.AddGeometryParameter("Geometries", "G", "Geometry objects in RhinoDoc to match against SsSilhouettes", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SelectedList", "SL", "Selected SsSilhouette objects", GH_ParamAccess.list);
            pManager.AddTextParameter("LogMessages", "LM", "Step-by-step log messages and summary report", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize inputs
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            List<IGH_Goo> geometryGoos = new List<IGH_Goo>();
            List<string> logMessages = new List<string> { "Starting ProjecteeSelect" };
            List<string> failureReasons = new List<string>();

            // Input validation
            if (!DA.GetDataList(0, silhouettes) || silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                failureReasons.Add("Failed to get SsSilhouettes.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(1, logTree);
                return;
            }
            if (!DA.GetDataList(1, geometryGoos) || geometryGoos == null || geometryGoos.Count == 0)
            {
                logMessages.Add("Error: Failed to get Geometries.");
                failureReasons.Add("Failed to get Geometries.");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(1, logTree);
                return;
            }

            logMessages.Add($"Inputs: {silhouettes.Count} silhouettes, {geometryGoos.Count} geometries");

            // Initialize outputs
            List<SsSilhouette> selectedList = new List<SsSilhouette>();
            HashSet<Guid> geometryGuids = new HashSet<Guid>();
            int failedMatches = 0;
            int failedGuids = 0;

            // Process geometries to extract Guids
            for (int i = 0; i < geometryGoos.Count; i++)
            {
                var goo = geometryGoos[i];
                if (!(goo is IGH_GeometricGoo geometricGoo))
                {
                    logMessages.Add($"Warning: Geometry at index {i} is not a geometric object.");
                    failureReasons.Add($"Geometry at index {i}: Not a geometric object.");
                    failedGuids++;
                    continue;
                }

                // Extract Guid from IGH_GeometricGoo
                Guid geoGuid = geometricGoo.ReferenceID;
                if (geoGuid == Guid.Empty)
                {
                    logMessages.Add($"Warning: Geometry at index {i} does not have a valid ReferenceID.");
                    failureReasons.Add($"Geometry at index {i}: Invalid ReferenceID.");
                    failedGuids++;
                    continue;
                }

                geometryGuids.Add(geoGuid);
                logMessages.Add($"Geometry at index {i} with Guid {geoGuid} added for matching.");
            }

            // Select SsSilhouettes based on ParentID matching
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null || silhouette.ParentID == Guid.Empty)
                {
                    logMessages.Add($"Warning: Silhouette CrvCode {silhouette?.CrvCode ?? -1} is null or has invalid ParentID.");
                    failureReasons.Add($"Silhouette CrvCode {silhouette?.CrvCode ?? -1}: Null or invalid ParentID.");
                    failedMatches++;
                    continue;
                }

                if (geometryGuids.Contains(silhouette.ParentID))
                {
                    selectedList.Add(silhouette);
                    logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode} matched with ParentID {silhouette.ParentID}.");
                }
                else
                {
                    logMessages.Add($"Warning: Silhouette CrvCode {silhouette.CrvCode} ParentID {silhouette.ParentID} not found in input geometries.");
                    failureReasons.Add($"Silhouette CrvCode {silhouette.CrvCode}: ParentID {silhouette.ParentID} not found in input geometries.");
                    failedMatches++;
                }
            }

            // Generate Summary Report
            List<string> summaryReport = new List<string>
            {
                $"Total Silhouettes Input: {silhouettes.Count}",
                $"Total Geometries Input: {geometryGoos.Count}",
                $"Valid Geometry Guids: {geometryGoos.Count - failedGuids}",
                $"Selected Silhouettes: {selectedList.Count}",
                $"Failed Matches: {failedMatches}",
                $"Failed Geometry Guids: {failedGuids}",
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
                DA.SetDataList(0, selectedList);
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0)); // Step-by-step logs
                logTree.AppendRange(summaryReport.Select(s => new GH_String(s)), new GH_Path(0, 1)); // Summary report
                DA.SetDataTree(1, logTree);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error: Setting outputs failed: {ex.Message}");
                var logTree = new GH_Structure<GH_String>();
                logTree.AppendRange(logMessages.Select(m => new GH_String(m)), new GH_Path(0, 0));
                DA.SetDataTree(1, logTree);
            }
        }


        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid
        {
            get { return new Guid("9F83F0B2-BF9A-4634-9847-AC38FF5D5123"); }
        }
    }
}