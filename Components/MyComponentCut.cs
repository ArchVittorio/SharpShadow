using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;

namespace SharpShadow.Components
{
    public class MyComponentCut : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TestMyComponent2 class.
        /// </summary>
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
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize inputs
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            SsSettings settings = null;
            List<string> logMessages = new List<string> { "Starting CutGeometry" };

            if (!DA.GetDataList(0, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                DA.SetDataList(5, logMessages);
                return;
            }

            if (!DA.GetData(1, ref settings) || settings == null)
            {
                logMessages.Add("Error: Failed to get SsSettings or settings is null.");
                DA.SetDataList(5, logMessages);
                return;
            }

            logMessages.Add($"Input: {silhouettes.Count} silhouettes, Tolerance: {settings.Tolerance}");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                DA.SetDataList(5, logMessages);
                return;
            }

            // Reset CrvCode
            SsSilhouette.ResetCrvCode();

            // Initialize output collections
            List<GeometryBase> jeongParts = new List<GeometryBase>();
            List<GeometryBase> jamParts = new List<GeometryBase>();
            List<GeometryBase> jingParts = new List<GeometryBase>();
            List<bool> isFailedCutters = new List<bool>();
            List<SsSilhouette> updatedSilhouettes = new List<SsSilhouette>();

            double tolerance = settings.Tolerance;

            // Step 1: Simple Cutting for all silhouettes
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null)
                {
                    logMessages.Add("Error: Null silhouette, skipping.");
                    updatedSilhouettes.Add(null);
                    isFailedCutters.Add(false);
                    continue;
                }

                if (silhouette.GeometryReference == null || !silhouette.GeometryReference.IsValid)
                {
                    logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Invalid or null GeometryReference.");
                    updatedSilhouettes.Add(silhouette);
                    isFailedCutters.Add(true);
                    silhouette.IsFailedCutter = true;
                    continue;
                }

                if (silhouette.SilCrv == null || !silhouette.SilCrv.IsValid)
                {
                    logMessages.Add($"Error: Silhouette CrvCode {silhouette.CrvCode}: Invalid or null SilCrv.");
                    updatedSilhouettes.Add(silhouette);
                    isFailedCutters.Add(true);
                    silhouette.IsFailedCutter = true;
                    continue;
                }

                try
                {
                    // Initialize parts
                    silhouette.JeongParts = new List<GeometryBase>();
                    silhouette.JamParts = new List<GeometryBase>();
                    silhouette.JingParts = new List<GeometryBase>();

                    // Simple Cutting
                    GeometryBase[] simpleParts = silhouette.DivideChiaroSicuro(tolerance, logMessages);
                    if (simpleParts != null && simpleParts.Length >= 2)
                    {
                        // Classify parts
                        silhouette.ClassifyJeong2JamParts(simpleParts, tolerance, logMessages);
                        silhouette.IsFailedCutter = false;
                    }
                    else
                    {
                        silhouette.IsFailedCutter = true;
                    }

                    updatedSilhouettes.Add(silhouette);
                    isFailedCutters.Add(silhouette.IsFailedCutter);
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Error in Silhouette CrvCode {silhouette.CrvCode}: Exception in simple cutting: {ex.Message}");
                    silhouette.IsFailedCutter = true;
                    updatedSilhouettes.Add(silhouette);
                    isFailedCutters.Add(true);
                }
            }

            // Step 2: Advanced Cutting for failed cases
            foreach (var silhouette in updatedSilhouettes.Where(s => s != null && s.IsFailedCutter))
            {
                try
                {
                    GeometryBase[] advancedParts = silhouette.DivideChiaroSicuroAdvanced(tolerance, logMessages);
                    if (advancedParts != null && advancedParts.Length >= 2)
                    {
                        // Classify parts
                        silhouette.ClassifyJeongMultiJamParts(advancedParts, tolerance, logMessages, out _, out _, out _);
                        silhouette.IsFailedCutter = false;
                    }
                    else
                    {
                        silhouette.IsFailedCutter = true;
                    }

                    // Update IsFailedCutter in the list
                    int idx = updatedSilhouettes.IndexOf(silhouette);
                    isFailedCutters[idx] = silhouette.IsFailedCutter;
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Exception in advanced cutting: {ex.Message}");
                    silhouette.IsFailedCutter = true;
                    int idx = updatedSilhouettes.IndexOf(silhouette);
                    isFailedCutters[idx] = true;
                }
            }

            // Step 3: Collect outputs
            foreach (var silhouette in updatedSilhouettes)
            {
                if (silhouette == null)
                    continue;

                // Jeong Parts
                if (silhouette.JeongParts != null && silhouette.JeongParts.Any())
                {
                    jeongParts.AddRange(silhouette.JeongParts);
                }
                else if (silhouette.IsFailedCutter)
                {
                    jeongParts.Add(silhouette.GeometryReference);
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

                // Jing Parts (empty)
                jingParts.AddRange(silhouette.JingParts ?? new List<GeometryBase>());
            }

            // Set outputs
            try
            {
                DA.SetDataList(0, jeongParts);
                DA.SetDataList(1, jamParts);
                DA.SetDataList(2, jingParts);
                DA.SetDataList(3, isFailedCutters);
                DA.SetDataList(4, updatedSilhouettes);
                DA.SetDataList(5, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(5, logMessages);
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("4C267BE0-64F0-4170-B3BE-393F4EF38D3A"); }
        }
    }
}