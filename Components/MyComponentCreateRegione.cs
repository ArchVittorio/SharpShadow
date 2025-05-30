using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
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
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            SsSettings settings = null;
            List<string> logMessages = new List<string>();
            logMessages.Add("Starting CreateSsRegione");

            if (!DA.GetDataList(0, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (!DA.GetData(1, ref settings))
            {
                logMessages.Add("Error: Failed to get SsSettings.");
                DA.SetDataList(2, logMessages);
                return;
            }

            logMessages.Add($"Inputs: {silhouettes.Count} silhouettes, ViewName: {settings?.ViewName ?? "null"}");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (settings == null)
            {
                logMessages.Add("Error: SsSettings is null.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (settings.Tolerance <= 0)
            {
                logMessages.Add("Error: Invalid tolerance in SsSettings.");
                DA.SetDataList(2, logMessages);
                return;
            }

            // Get camera plane
            if (!settings.GetCameraPlane(out Plane testPlane, out string planeLog))
            {
                logMessages.Add(planeLog);
                DA.SetDataList(2, logMessages);
                return;
            }
            logMessages.Add(planeLog);

            // Generate SsRegione objects
            SsRegione.ResetNumber();
            logMessages.Add("Starting SsRegione creation");
            List<SsRegione> regions = null;
            List<Curve> regionCurves = new List<Curve>();
            try
            {
                regions = SsRegione.CreateRegionsFromSilhouettes(silhouettes, testPlane, settings.Tolerance, out var regionLogs);
                logMessages.AddRange(regionLogs);
                logMessages.Add($"Created {regions?.Count ?? 0} regions");

                if (regions != null)
                {
                    foreach (var region in regions)
                    {
                        if (region == null)
                        {
                            logMessages.Add("Warning: Null region encountered.");
                            regionCurves.Add(null);
                            continue;
                        }
                        regionCurves.Add(region.RegionCurve);
                        logMessages.Add($"Region {region.RegionCode}: RegionCurve {(region.RegionCurve != null ? "computed" : "null")}.");
                    }
                }
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception in SsRegione creation: {ex.Message}");
                DA.SetDataList(2, logMessages);
                return;
            }

            // Set outputs
            try
            {
                var regionTree = new Grasshopper.DataTree<Curve>();
                for (int i = 0; i < regionCurves.Count; i++)
                {
                    regionTree.Add(regionCurves[i], new Grasshopper.Kernel.Data.GH_Path(i));
                }
                DA.SetDataTree(0, regionTree);
                DA.SetDataList(1, regions);
                DA.SetDataList(2, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(2, logMessages);
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

        public override Guid ComponentGuid
        {
            get { return new Guid("C76D8ABE-3EA4-40C2-90F9-56B4799308D5"); }
        }
    }
}