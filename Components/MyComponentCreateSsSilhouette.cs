using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace SharpShadow.Components
{
    public class CreateSsSilhouetteComponent : GH_Component
    {

 
        public CreateSsSilhouetteComponent()
            : base("CreateSsSilhouette", "CreateSil",
                   "Creates SsSilhouette objects from geometries and SsSettings.",
                   "SharpShadow", "Silhouette")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometries", "G", "Input geometries (Brep or Mesh)", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSettings", "S", "SsSettings object from CreateSsSettings", GH_ParamAccess.item);
            pManager.AddBooleanParameter("CreateShadow", "CS", "Compute shadow-related data (default: true)", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("SilCrv", "SC", "Silhouette curves", GH_ParamAccess.list);
            pManager.AddCurveParameter("PlanedCrv", "PC", "Planed curves", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects", GH_ParamAccess.list);
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GeometryBase> geometries = new List<GeometryBase>();
            SsSettings settings = null;
            bool createShadow = true;
            List<string> logMessages = new List<string>();
            logMessages.Add("Starting CreateSsSilhouette");

            if (!DA.GetDataList(0, geometries))
            {
                logMessages.Add("Error: Failed to get Geometries.");
                DA.SetDataList(3, logMessages);
                return;
            }
            if (!DA.GetData(1, ref settings))
            {
                logMessages.Add("Error: Failed to get SsSettings.");
                DA.SetDataList(3, logMessages);
                return;
            }
            DA.GetData(2, ref createShadow);

            logMessages.Add($"Inputs: {geometries.Count} geometries, ViewName: {settings?.ViewName ?? "null"}, CreateShadow: {createShadow}");

            if (geometries == null || geometries.Count == 0)
            {
                logMessages.Add("Error: No geometries provided.");
                DA.SetDataList(3, logMessages);
                return;
            }
            if (settings == null)
            {
                logMessages.Add("Error: SsSettings is null.");
                DA.SetDataList(3, logMessages);
                return;
            }
            if (settings.Tolerance <= 0)
            {
                logMessages.Add("Error: Invalid tolerance in SsSettings.");
                DA.SetDataList(3, logMessages);
                return;
            }

            // Generate SsSilhouette objects
            SsSilhouette.ResetCrvCode();
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            List<Curve> silCrvs = new List<Curve>();
            List<Curve> planedCrvs = new List<Curve>();
            logMessages.Add("Starting SsSilhouette creation");

            for (int i = 0; i < geometries.Count; i++)
            {
                var geometry = geometries[i];
                if (geometry == null || !geometry.IsValid)
                {
                    logMessages.Add($"Geometry {i}: Invalid or null geometry.");
                    continue;
                }

                try
                {
                    // Assume SsSilhouette constructor handles CreateShadow logic
                    var silhouette = new SsSilhouette(
                        geometry,
                        SilhouetteType.Boundary,
                        settings.Tolerance,
                        settings.AngleToleranceRadians,
                        settings.ViewName,
                        createShadow, // Pass CreateShadow parameter
                        out var silhouetteLogs
                    );
                    silhouettes.Add(silhouette);
                    if (silhouette.SilCrv != null && silhouette.SilCrv.IsValid)
                    {
                        silCrvs.Add(silhouette.SilCrv);
                        logMessages.Add($"Geometry {i}: SilCrv computed.");
                    }
                    else
                    {
                        silCrvs.Add(null);
                        logMessages.Add($"Geometry {i}: No SilCrv computed.");
                    }
                    if (silhouette.PlanedCurve != null && silhouette.PlanedCurve.IsValid)
                    {
                        planedCrvs.Add(silhouette.PlanedCurve);
                        logMessages.Add($"Geometry {i}: PlanedCurve computed.");
                    }
                    else
                    {
                        planedCrvs.Add(null);
                        logMessages.Add($"Geometry {i}: No PlanedCurve computed.");
                    }
                    logMessages.AddRange(silhouetteLogs);
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Geometry {i}: Exception in SsSilhouette creation: {ex.Message}");
                    silCrvs.Add(null);
                    planedCrvs.Add(null);
                }
            }
            logMessages.Add($"Created {silhouettes.Count} silhouettes");

            // Set outputs
            try
            {
                DA.SetDataList(0, silCrvs);
                DA.SetDataList(1, planedCrvs);
                DA.SetDataList(2, silhouettes);
                DA.SetDataList(3, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(3, logMessages);
            }
        }

        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-1238567890ab");
    }
}