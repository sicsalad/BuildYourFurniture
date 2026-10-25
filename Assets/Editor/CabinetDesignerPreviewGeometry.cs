#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // The outside polygon is the finished size. Core is offset inward on banded edges.
    // Edge strips are separate, closed 3D meshes and can have a different material.
    public static class CabinetDesignerPreviewGeometry
    {
        public static List<GameObject> Build(CabinetDesignerDocument.PanelElement panel,Color coreColor,Color bandColor)
        {
            var result=new List<GameObject>();
            try
            {
                var raw=CabinetDesignerDocument.RawOutline(panel);
                var finished=panel.Outline.Select(v=>v.Point).ToArray();
                if(raw.Length!=finished.Length)throw new InvalidOperationException("Outline mismatch");
                var core=LShapePanelMesh.Create(panel.Name+"_core",raw.Select(v=>v*.001f).ToList(),
                    panel.Thickness*.001f,panel.Position*.001f,0,coreColor);
                core.transform.rotation=Quaternion.Euler(panel.Rotation);
                result.Add(core);
                // Each strip is a closed prism bounded by the finished and raw edge.
                for(int i=0;i<finished.Length;i++)
                {
                    float band=panel.Bands.ContainsKey(panel.Outline[i].Id)?panel.Bands[panel.Outline[i].Id]:0;
                    if(band<=0)continue;
                    int j=(i+1)%finished.Length;
                    var candidates=new[]{finished[i],finished[j],raw[j],raw[i]};
                    var strip=new List<Vector2>();
                    foreach(var point in candidates)
                        if(strip.Count==0||(strip[strip.Count-1]-point).sqrMagnitude>.000001f)strip.Add(point);
                    if(strip.Count>1&&(strip[0]-strip[strip.Count-1]).sqrMagnitude<=.000001f)strip.RemoveAt(strip.Count-1);
                    if(strip.Count<3)continue;
                    float signedArea=0;
                    for(int k=0;k<strip.Count;k++)signedArea+=strip[k].x*strip[(k+1)%strip.Count].y-strip[(k+1)%strip.Count].x*strip[k].y;
                    if(Mathf.Abs(signedArea)<.001f)continue;
                    var go=LShapePanelMesh.Create(panel.Name+"_band_"+panel.Outline[i].Id,
                        strip.Select(v=>v*.001f).ToList(),panel.Thickness*.001f,
                        panel.Position*.001f,0,bandColor);
                    go.transform.rotation=Quaternion.Euler(panel.Rotation);
                    result.Add(go);
                }
                return result;
            }
            catch
            {
                foreach(var go in result)if(go!=null)UnityEngine.Object.DestroyImmediate(go);
                throw;
            }
        }
    }
}
#endif