using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpShadow.Components
{
    public class CutGeometryComponent : GH_Component
    {

        
        public CutGeometryComponent()
            : base("CutGeometry", "CutGeo",
                   "Cuts GeometryReference using SilCrv and classifies parts.",
                   "SharpShadow", "Silhouette")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("SimpleJeongParts", "SJ", "Jeong parts from simple cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("SimpleJamParts", "SM", "Jam parts from simple cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("AdvancedJeongParts", "AJ", "Jeong parts from advanced cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("AdvancedJamParts", "AM", "Jam parts from advanced cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("AdvancedJingParts", "AG", "Jing parts from advanced cutting", GH_ParamAccess.list);
            pManager.AddGeometryParameter("FailedGeometries", "FG", "Geometries that failed cutting", GH_ParamAccess.list);
            pManager.AddGenericParameter("UpdatedSsSilhouettes", "US", "SsSilhouettes with updated parts", GH_ParamAccess.list);
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            List<string> logMessages = new List<string>();
            logMessages.Add("Starting CutGeometry");

            if (!DA.GetDataList(0, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                DA.SetDataList(7, logMessages);
                return;
            }

            logMessages.Add($"Inputs: {silhouettes.Count} silhouettes");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                DA.SetDataList(7, logMessages);
                return;
            }

            List<GeometryBase> simpleJeongParts = new List<GeometryBase>();
            List<GeometryBase> simpleJamParts = new List<GeometryBase>();
            List<GeometryBase> advancedJeongParts = new List<GeometryBase>();
            List<GeometryBase> advancedJamParts = new List<GeometryBase>();
            List<GeometryBase> advancedJingParts = new List<GeometryBase>();
            List<GeometryBase> failedGeometries = new List<GeometryBase>();
            List<SsSilhouette> updatedSilhouettes = new List<SsSilhouette>();

            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null)
                {
                    logMessages.Add("Silhouette: Null silhouette, skipping.");
                    updatedSilhouettes.Add(null);
                    continue;
                }

                if (silhouette.GeometryReference == null || !silhouette.GeometryReference.IsValid)
                {
                    logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Invalid or null GeometryReference.");
                    updatedSilhouettes.Add(silhouette);
                    failedGeometries.Add(null);
                    continue;
                }

                if (silhouette.SilCrv == null || !silhouette.SilCrv.IsValid)
                {
                    logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Invalid or null SilCrv.");
                    updatedSilhouettes.Add(silhouette);
                    failedGeometries.Add(silhouette.GeometryReference);
                    continue;
                }

                try
                {
                    // Simple cutting (placeholder logic)
                    List<GeometryBase> sJeong = new List<GeometryBase>();
                    List<GeometryBase> sJam = new List<GeometryBase>();
                    if (silhouette.GeometryReference is Brep brep)
                    {
                        // Example: Split Brep with SilCrv
                        Brep[] splitBreps = brep.Split(new[] { silhouette.SilCrv }, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
                        if (splitBreps != null && splitBreps.Length > 0)
                        {
                            sJeong.Add(splitBreps[0]); // Assume first part is Jeong
                            if (splitBreps.Length > 1)
                                sJam.Add(splitBreps[1]); // Second part is Jam
                            logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Simple cutting produced {splitBreps.Length} parts.");
                        }
                        else
                        {
                            logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Simple cutting failed.");
                            failedGeometries.Add(silhouette.GeometryReference);
                        }
                    }
                    else
                    {
                        logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Simple cutting not supported for {silhouette.ParentType}.");
                        failedGeometries.Add(silhouette.GeometryReference);
                    }

                    // Advanced cutting (placeholder logic)
                    List<GeometryBase> aJeong = new List<GeometryBase>();
                    List<GeometryBase> aJam = new List<GeometryBase>();
                    List<GeometryBase> aJing = new List<GeometryBase>();
                    // Example: More sophisticated splitting or classification
                    // TODO: Implement advanced cutting logic (e.g., ray-casting, volume analysis)
                    if (silhouette.GeometryReference is Brep advBrep)
                    {
                        // Placeholder: Reuse simple split for demonstration
                        Brep[] advSplitBreps = advBrep.Split(new[] { silhouette.SilCrv }, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
                        if (advSplitBreps != null && advSplitBreps.Length > 0)
                        {
                            aJeong.Add(advSplitBreps[0]);
                            if (advSplitBreps.Length > 1)
                                aJam.Add(advSplitBreps[1]);
                            if (advSplitBreps.Length > 2)
                                aJing.Add(advSplitBreps[2]); // Hypothetical Jing part
                            logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Advanced cutting produced {advSplitBreps.Length} parts.");
                        }
                        else
                        {
                            logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Advanced cutting failed.");
                            if (!failedGeometries.Contains(silhouette.GeometryReference))
                                failedGeometries.Add(silhouette.GeometryReference);
                        }
                    }
                    else
                    {
                        logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Advanced cutting not supported for {silhouette.ParentType}.");
                        if (!failedGeometries.Contains(silhouette.GeometryReference))
                            failedGeometries.Add(silhouette.GeometryReference);
                    }

                    // Update SsSilhouette parts
                    silhouette.JeongParts = aJeong.Any() ? aJeong : sJeong;
                    silhouette.JamParts = aJam.Any() ? aJam : sJam;
                    silhouette.JingParts = aJing;
                    updatedSilhouettes.Add(silhouette);

                    // Collect outputs
                    simpleJeongParts.AddRange(sJeong);
                    simpleJamParts.AddRange(sJam);
                    advancedJeongParts.AddRange(aJeong);
                    advancedJamParts.AddRange(aJam);
                    advancedJingParts.AddRange(aJing);
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Silhouette CrvCode {silhouette.CrvCode}: Exception in cutting: {ex.Message}");
                    updatedSilhouettes.Add(silhouette);
                    failedGeometries.Add(silhouette.GeometryReference);
                }
            }

            // Set outputs
            try
            {
                DA.SetDataList(0, simpleJeongParts);
                DA.SetDataList(1, simpleJamParts);
                DA.SetDataList(2, advancedJeongParts);
                DA.SetDataList(3, advancedJamParts);
                DA.SetDataList(4, advancedJingParts);
                DA.SetDataList(5, failedGeometries);
                DA.SetDataList(6, updatedSilhouettes);
                DA.SetDataList(7, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(7, logMessages);
            }
        }

        public override Guid ComponentGuid => new Guid("b2c3d4e5-f6a7-8901-abcd-2345878901ab");
    }
}