using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Display;
using Rhino;
using Rhino.Geometry;

namespace SharpShadow.Components
{
    public class MyComponent1 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MyComponent1()
         : base("CreateSsSettings", "CreateSettings",
                   "Creates SsSettings with viewport camera plane and direction.",
                   "SharpShadow", "Setting")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("ViewName", "V", "Viewport name (e.g., Perspective2)", GH_ParamAccess.item, "Perspective2");
            pManager.AddNumberParameter("Tolerance", "T", "Length tolerance for computations", GH_ParamAccess.item, 0.001);
            pManager.AddNumberParameter("AngleTolerance", "A", "Angle tolerance in degrees", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SsSettings", "S", "SsSettings object with viewport settings", GH_ParamAccess.item);
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize inputs
            string viewName = "Perspective2";
            double tolerance = 0.001;
            double angleToleranceDegrees = 1.0;
            List<string> logMessages = new List<string>();
            logMessages.Add("Starting CreateSsSettings");

            // Get inputs
            if (!DA.GetData(0, ref viewName))
            {
                logMessages.Add("Error: Failed to get ViewName.");
                DA.SetDataList(1, logMessages);
                return;
            }
            if (!DA.GetData(1, ref tolerance))
            {
                logMessages.Add("Error: Failed to get Tolerance.");
                DA.SetDataList(1, logMessages);
                return;
            }
            if (!DA.GetData(2, ref angleToleranceDegrees))
            {
                logMessages.Add("Error: Failed to get AngleTolerance.");
                DA.SetDataList(1, logMessages);
                return;
            }

            logMessages.Add($"Inputs: ViewName: {viewName}, Tolerance: {tolerance}, AngleTolerance: {angleToleranceDegrees}");

            // Validate inputs
            if (string.IsNullOrEmpty(viewName))
            {
                logMessages.Add("Error: ViewName is empty.");
                DA.SetDataList(1, logMessages);
                return;
            }
            if (tolerance <= 0)
            {
                logMessages.Add("Error: Tolerance must be positive.");
                DA.SetDataList(1, logMessages);
                return;
            }
            if (angleToleranceDegrees <= 0)
            {
                logMessages.Add("Error: AngleTolerance must be positive.");
                DA.SetDataList(1, logMessages);
                return;
            }

            // Create SsSettings
            SsSettings settings = new SsSettings
            {
                ViewName = viewName,
                Tolerance = tolerance,
                AngleToleranceRadians = RhinoMath.ToRadians(angleToleranceDegrees)
            };

            // Get camera direction
            if (!settings.GetCameraDirection(out Vector3d direction, out string directionLog))
            {
                logMessages.Add(directionLog);
                DA.SetDataList(1, logMessages);
                return;
            }
            logMessages.Add(directionLog);

            // Get camera near plane
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            Plane testPlane = new Plane();
            RhinoView view = doc.Views.Find(viewName, true);
            if (view != null && view.ActiveViewport.GetFrustumNearPlane(out Plane nearPlane))
            {
                testPlane = nearPlane;
                logMessages.Add($"Test plane set: Normal = {nearPlane.Normal.X}, {nearPlane.Normal.Y}, {nearPlane.Normal.Z}");
            }
            else
            {
                logMessages.Add("Error: Invalid view or near plane, using default plane.");
            }

            // Set outputs
            try
            {
                DA.SetData(0, settings);
                logMessages.Add("Output: SsSettings created successfully.");
                DA.SetDataList(1, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(1, logMessages);
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
            get { return new Guid("EAC97E2C-6A92-4CDB-BC6E-6BF37165C9B4"); }
        }
    }
}