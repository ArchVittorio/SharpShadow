using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.DocObjects;

namespace SharpShadow.Components{ 
public class MyComponentGUID : GH_Component
{
    public MyComponentGUID()
        : base("Get Object GUID", "GetGUID", "获取Rhino几何对象的GUID", "SharpShadow", "Silhouette")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // 输入参数：几何对象
        pManager.AddGeometryParameter("Geometry", "G", "输入的几何对象", GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // 输出参数：GUID列表
        pManager.AddTextParameter("GUIDs", "ID", "几何对象的GUID", GH_ParamAccess.list);
        pManager.AddTextParameter("Object Info", "Info", "对象信息", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        List<IGH_GeometricGoo> geometryList = new List<IGH_GeometricGoo>();

        // 获取输入的几何对象
        if (!DA.GetDataList(0, geometryList))
            return;

        List<string> guidList = new List<string>();
        List<string> infoList = new List<string>();

        foreach (var goo in geometryList)
        {
            string guid = "No GUID";
            string info = "Unknown";

            try
            {
                // 方法1：检查是否为GH_Brep
                if (goo is GH_Brep brepGoo)
                {
                    if (brepGoo.ReferenceID != Guid.Empty)
                    {
                        guid = brepGoo.ReferenceID.ToString();
                        info = $"Brep Reference";
                    }
                    else
                    {
                        info = $"Direct Brep";
                    }
                }
                // 方法2：检查是否为GH_Mesh
                else if (goo is GH_Mesh meshGoo)
                {
                    if (meshGoo.ReferenceID != Guid.Empty)
                    {
                        guid = meshGoo.ReferenceID.ToString();
                        info = $"Mesh Reference";
                    }
                    else
                    {
                        info = $"Direct Mesh";
                    }
                }
                // 方法3：检查是否为GH_Curve
                else if (goo is GH_Curve curveGoo)
                {
                    if (curveGoo.ReferenceID != Guid.Empty)
                    {
                        guid = curveGoo.ReferenceID.ToString();
                        info = $"Curve Reference";
                    }
                    else
                    {
                        info = $"Direct Curve";
                    }
                }
                // 方法4：检查是否为GH_Surface
                else if (goo is GH_Surface surfaceGoo)
                {
                    if (surfaceGoo.ReferenceID != Guid.Empty)
                    {
                        guid = surfaceGoo.ReferenceID.ToString();
                        info = $"Surface Reference";
                    }
                    else
                    {
                        info = $"Direct Surface";
                    }
                }
                // 方法5：通用方法 - 使用反射检查ReferenceID属性
                else
                {
                    var referenceIdProperty = goo.GetType().GetProperty("ReferenceID");
                    if (referenceIdProperty != null)
                    {
                        var referenceId = referenceIdProperty.GetValue(goo);
                        if (referenceId is Guid refGuid && refGuid != Guid.Empty)
                        {
                            guid = refGuid.ToString();
                            info = $"Generic Reference {goo.GetType().Name}";
                        }
                        else
                        {
                            info = $"Direct {goo.GetType().Name}";
                        }
                    }
                    else
                    {
                        info = $"Unknown {goo.GetType().Name}";
                    }
                }
            }
            catch (Exception ex)
            {
                info = $"Error: {ex.Message}";
            }

            guidList.Add(guid);
            infoList.Add(info);
        }

        DA.SetDataList(0, guidList);
        DA.SetDataList(1, infoList);
    }



    public override Guid ComponentGuid => new Guid("12345678-1234-1234-1234-123456789abc");
}

// 扩展方法：更直接的方式获取GUID
public static class GrasshopperExtensions
{
    public static List<Guid> GetRhinoObjectGuids(this List<IGH_GeometricGoo> geometryGoos)
    {
        List<Guid> guids = new List<Guid>();

        foreach (var goo in geometryGoos)
        {
            Guid guid = GetRhinoObjectGuid(goo);
            guids.Add(guid);
        }

        return guids;
    }

    public static Guid GetRhinoObjectGuid(this IGH_GeometricGoo goo)
    {
        if (goo == null) return Guid.Empty;

        // 检查常见的几何类型
        if (goo is GH_Brep brep && brep.ReferenceID != Guid.Empty)
            return brep.ReferenceID;

        if (goo is GH_Mesh mesh && mesh.ReferenceID != Guid.Empty)
            return mesh.ReferenceID;

        if (goo is GH_Curve curve && curve.ReferenceID != Guid.Empty)
            return curve.ReferenceID;

        if (goo is GH_Surface surface && surface.ReferenceID != Guid.Empty)
            return surface.ReferenceID;

        // 通用方法：使用反射检查ReferenceID属性
        var referenceIdProperty = goo.GetType().GetProperty("ReferenceID");
        if (referenceIdProperty != null)
        {
            var referenceId = referenceIdProperty.GetValue(goo);
            if (referenceId is Guid refGuid && refGuid != Guid.Empty)
            {
                return refGuid;
            }
        }

        return Guid.Empty;
    }
}

}