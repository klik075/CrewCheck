using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>룸 아이콘, 원형 초상화 마스크와 황동 액자.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoomHelmGraphic : MaskableGraphic
    {
        public enum Symbol { Helm, Exit, Settings, Invite, Group, Lock, Chat, Crown, Ready, Waiting, Medallion, Disc, EmptySlot, Pill }
        private Symbol shape;
        public Symbol Shape { get => shape; set { shape = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 c = rect.center;
            float r = Mathf.Min(rect.width, rect.height) * .46f;
            if (shape == Symbol.Pill)
            {
                float radius = rect.height * .45f;
                var points = new Vector2[36];
                for (int i = 0; i < 36; i++)
                {
                    int corner = i / 9;
                    float angle = (corner * 90 + i % 9 * 90f / 8) * Mathf.Deg2Rad;
                    Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                        corner < 2 ? rect.yMax - radius : rect.yMin + radius);
                    points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
                Polygon(mesh, Vector2.zero, 1, points, color); return;
            }
            if (shape == Symbol.Disc) { Disc(mesh, c, r, color); return; }
            if (shape == Symbol.Medallion)
            {
                Ring(mesh, c, r, 5, new Color32(71, 40, 19, 255));
                Ring(mesh, c, r - 2, 4, new Color32(214, 167, 80, 255), true);
                Ring(mesh, c, r - 5, 1.5f, new Color32(255, 227, 156, 255));
                Ring(mesh, c, r - 7, 2, new Color32(71, 40, 19, 255));
                return;
            }
            if (shape == Symbol.EmptySlot)
            {
                Ring(mesh, c, r * .86f, 1.2f, color, false, true);
                Line(mesh, c, r, -.28f, 0, .28f, 0, 2.5f);
                Line(mesh, c, r, 0, -.28f, 0, .28f, 2.5f);
                return;
            }
            r *= .82f;
            float stroke = Mathf.Max(1.5f, rect.width * .065f);
            if (shape == Symbol.Ready)
            {
                Disc(mesh, c, r, color);
                Stroke(mesh, c + new Vector2(-.46f, .02f) * r, c + new Vector2(-.12f, -.32f) * r, stroke, Color.white);
                Stroke(mesh, c + new Vector2(-.12f, -.32f) * r, c + new Vector2(.5f, .38f) * r, stroke, Color.white);
                return;
            }
            if (shape == Symbol.Waiting) { Disc(mesh, c, r * .67f, color); return; }
            if (shape == Symbol.Crown)
            {
                Polygon(mesh, c, r, new[] { new Vector2(-.65f,-.5f), new Vector2(-.75f,-.05f), new Vector2(.75f,-.05f), new Vector2(.65f,-.5f) }, color);
                Polygon(mesh, c, r, new[] { new Vector2(-.75f,-.1f), new Vector2(-.9f,.65f), new Vector2(-.2f,-.1f) }, color);
                Polygon(mesh, c, r, new[] { new Vector2(-.45f,-.1f), new Vector2(0,.9f), new Vector2(.45f,-.1f) }, color);
                Polygon(mesh, c, r, new[] { new Vector2(.2f,-.1f), new Vector2(.9f,.65f), new Vector2(.75f,-.1f) }, color);
                Line(mesh,c,r,-.68f,-.72f,.68f,-.72f,stroke);
                return;
            }
            if (shape == Symbol.Exit)
            {
                Line(mesh,c,r,-.9f,-.9f,-.9f,.9f,stroke); Line(mesh,c,r,-.9f,.9f,.2f,.9f,stroke);
                Line(mesh,c,r,-.9f,-.9f,.2f,-.9f,stroke); Line(mesh,c,r,-.25f,0,1,0,stroke);
                Line(mesh,c,r,1,0,.5f,.5f,stroke); Line(mesh,c,r,1,0,.5f,-.5f,stroke); return;
            }
            if (shape == Symbol.Invite || shape == Symbol.Group)
            {
                if (shape == Symbol.Group)
                {
                    Person(mesh,c + Vector2.left * r * .65f,r*.68f);
                    Person(mesh,c + Vector2.right * r * .65f,r*.68f);
                }
                Person(mesh,c + Vector2.left * r * .2f,r);
                if (shape == Symbol.Invite)
                {
                    Line(mesh,c,r,.72f,-.15f,.72f,-.9f,stroke); Line(mesh,c,r,.34f,-.52f,1.1f,-.52f,stroke);
                }
                return;
            }
            if (shape == Symbol.Lock)
            {
                Ring(mesh,c + Vector2.up*r*.35f,r*.55f,stroke,color);
                Polygon(mesh,c,r,new[] {new Vector2(-.85f,.2f),new Vector2(.85f,.2f),new Vector2(.85f,-.9f),new Vector2(-.85f,-.9f)},color);
                Disc(mesh,c + Vector2.down*r*.3f,r*.12f,new Color32(40,26,15,255)); return;
            }
            if (shape == Symbol.Chat)
            {
                Disc(mesh,c,r,color);
                Polygon(mesh,c,r,new[] {new Vector2(-.7f,-.5f),new Vector2(-.85f,-1.1f),new Vector2(.1f,-.7f)},color);
                for(int i=-1;i<=1;i++) Disc(mesh,c + Vector2.right * r * i * .42f,r*.1f,new Color32(50,30,18,255));
                return;
            }
            Ring(mesh,c,r*.82f,stroke*.8f,color);
            if(shape == Symbol.Settings) Ring(mesh,c,r*.34f,stroke*.8f,color);
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4; Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                Stroke(mesh,c+d*(shape == Symbol.Settings ? r*.65f : 0),c+d*r*1.12f,stroke,color);
            }
        }

        private void Person(VertexHelper mesh, Vector2 c, float r)
        {
            Disc(mesh,c + Vector2.up*r*.48f,r*.35f,color);
            Polygon(mesh,c,r,new[] {new Vector2(-.57f,-.85f),new Vector2(-.5f,-.2f),new Vector2(0,.04f),new Vector2(.5f,-.2f),new Vector2(.57f,-.85f)},color);
        }
        private void Line(VertexHelper mesh,Vector2 c,float r,float x1,float y1,float x2,float y2,float w)
            => Stroke(mesh,c+new Vector2(x1,y1)*r,c+new Vector2(x2,y2)*r,w,color);
        private static void Disc(VertexHelper mesh,Vector2 c,float r,Color tint)
        {
            int start=mesh.currentVertCount; mesh.AddVert(c,tint,Vector2.zero);
            for(int i=0;i<=96;i++)
            {
                float a=i*Mathf.PI/48; mesh.AddVert(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,tint,Vector2.zero);
                if(i>0)mesh.AddTriangle(start,start+i,start+i+1);
            }
        }
        private static void Ring(VertexHelper mesh,Vector2 c,float r,float w,Color tint,bool metallic=false,bool dashed=false)
        {
            for(int i=0;i<96;i++)
            {
                if(dashed && i%6>=3)continue;
                float a=i*Mathf.PI/48,b=(i+1)*Mathf.PI/48;
                Vector2 da=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),db=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                Color ca=metallic? Color.Lerp(tint*.52f,Color.Lerp(tint,Color.white,.42f),(.5f+.5f*Mathf.Sin(a+.5f))):tint;
                Color cb=metallic? Color.Lerp(tint*.52f,Color.Lerp(tint,Color.white,.42f),(.5f+.5f*Mathf.Sin(b+.5f))):tint;
                ca.a=cb.a=tint.a;
                int n=mesh.currentVertCount;
                mesh.AddVert(c+da*(r-w),ca,Vector2.zero);mesh.AddVert(c+da*r,ca,Vector2.zero);
                mesh.AddVert(c+db*r,cb,Vector2.zero);mesh.AddVert(c+db*(r-w),cb,Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
            }
        }
        private static void Polygon(VertexHelper mesh,Vector2 c,float scale,Vector2[] points,Color tint)
        {
            int n=mesh.currentVertCount;
            foreach(var p in points)mesh.AddVert(c+p*scale,tint,Vector2.zero);
            for(int i=1;i<points.Length-1;i++)mesh.AddTriangle(n,n+i,n+i+1);
        }
        private static void Stroke(VertexHelper mesh,Vector2 a,Vector2 b,float w,Color tint)
        {
            Vector2 d=(b-a).normalized,n=new Vector2(-d.y,d.x)*w*.5f; int start=mesh.currentVertCount;
            mesh.AddVert(a-n,tint,Vector2.zero);mesh.AddVert(a+n,tint,Vector2.zero);mesh.AddVert(b+n,tint,Vector2.zero);mesh.AddVert(b-n,tint,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
        protected override void Awake(){base.Awake();raycastTarget=false;}
    }
}
