using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry.Intersect;

namespace SharpShadow
{
    public class SsSettings
    {
        public double Tolerance { get; set; }
        public double AngleToleranceRadians { get; set; }
        public string ViewName { get; set; }
        public SsSettings()
        {
            Tolerance = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            AngleToleranceRadians = RhinoMath.ToRadians(3.0);
            ViewName = "";
        }

        public bool GetCameraDirection(out Vector3d direction, out string logMessage)
        {
            direction = Vector3d.Unset;
            logMessage = "";

            if (string.IsNullOrEmpty(ViewName))
            {
                logMessage = "Error: ViewName is empty.";
                return false;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            RhinoView view = doc.Views.Find(ViewName, true);
            if (view == null)
            {
                logMessage = $"Error: View '{ViewName}' not found.";
                return false;
            }

            RhinoViewport viewport = view.ActiveViewport;
            direction = viewport.CameraDirection;
            if (!direction.IsValid)
            {
                logMessage = $"Error: Invalid camera direction for view '{ViewName}'.";
                return false;
            }

            logMessage = $"Camera direction for view '{ViewName}': {direction}";
            return true;
        }

        public bool GetCameraPlane(out Plane plane, out string logMessage)
        {
            plane = new Plane();
            logMessage = "";

            if (string.IsNullOrEmpty(ViewName))
            {
                logMessage = "Error: ViewName is empty.";
                return false;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            RhinoView view = doc.Views.Find(ViewName, true);
            if (view == null)
            {
                logMessage = $"Error: View '{ViewName}' not found.";
                return false;
            }

            RhinoViewport viewport = view.ActiveViewport;
            if (!viewport.GetFrustumNearPlane(out Plane nearPlane))
            {
                logMessage = $"Error: Failed to get near plane for view '{ViewName}'.";
                return false;
            }

            plane = nearPlane;
            logMessage = $"Camera plane for view '{ViewName}': Normal = {nearPlane.Normal}, Origin = {nearPlane.Origin}";
            return true;
        }
    }
   
}
