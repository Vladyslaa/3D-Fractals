#version 460 core

out vec4 FragColor;
in vec2 vUV;

struct Object 
{
    vec4 type;
    vec4 pos;
    vec4 size;
    vec4 color;
    mat4 rotMatrix;
};

struct Hit
{
    float dist;
    int id;
};

struct BVHNode 
{
    vec4 bMinW; // x,y,z - min; w - leftChild
    vec4 bMaxW; // x,y,z - max; w - rightChild
};

layout(std430, binding = 0) buffer ObjectBuffer 
{
    int objectCount;
    Object objects[];
};

layout(std430, binding = 1) buffer BufferBVH 
{
    BVHNode nodes[];
};

uniform vec2 camResolution;
uniform vec3 camPos;
uniform vec3 camFront;
uniform vec3 camUp;
uniform vec3 camRight;

uniform vec3 globalLightDir;

float sdSphere(vec3 p, float r) 
{
    return length(p) - r;
}

float sdBox(vec3 p, vec3 b) 
{
    vec3 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x,max(d.y,d.z)), 0.0);
}

float sdBoxFast(vec3 p, vec3 b) 
{
    vec3 d = abs(p) - b;
    return length(max(d, 0.0)); 
}

float sdTorus(vec3 p, vec2 t)
{
    vec2 q = vec2(length(p.xz) - t.x / 1.5, p.y);
    return length(q) - t.y / 3;
}

float sdHexagon(vec3 p, vec2 h)
{
    const vec3 k = vec3(-0.8660254, 0.5, 0.57735);
    p = abs(p);
    p.xy -= 2.0*min(dot(k.xy, p.xy), 0.0)*k.xy;
    vec2 d = vec2(length(p.xy-vec2(clamp(p.x,-k.z*h.x,k.z*h.x), h.x))*sign(p.y-h.x), p.z-h.y );
    return min(max(d.x,d.y),0.0) + length(max(d,0.0));
}

float sdMandelbulb(vec3 p) 
{
    vec3 z = p;
    float dr = 1.0;
    float r = 0.0;
    float Power = 8.0;

    for (int i = 0; i < 10; i++) 
    {
        r = length(z);
        if (r > 2.0) break;
        
        float theta = acos(z.z / r);
        float phi = atan(z.y, z.x);
        dr = pow(r, Power - 1.0) * Power * dr + 1.0;

        float zr = pow(r, Power);
        theta = theta * Power;
        phi = phi * Power;
        
        z = zr * vec3(sin(theta) * cos(phi), sin(phi) * sin(theta), cos(theta));
        z += p;
    }
    return 0.5 * log(r) * r / dr;
}


float sdMengerSponge(vec3 p, vec3 size) 
{
    float d = sdBox(p, size); 
    float res = d;

    float s = 1.0;
    for(int m = 0; m < 4; m++) 
    {
        vec3 a = mod(p * s / size.x, 2.0) - 1.0; 
        s *= 3.0;
        vec3 r = abs(1.0 - 3.0 * abs(a));

        float da = max(r.x, r.y);
        float db = max(r.y, r.z);
        float dc = max(r.z, r.x);
        float c = (min(da, min(db, dc)) - 1.0) / s;

        res = max(res, c * size.x); 
    }
    return res;
}

float sdSierpinskiTetrahedron(vec3 p, float size) 
{
    p /= size;

    const float Scale = 2.0;
    const vec3 v1 = vec3( 1,  1,  1);
    const vec3 v2 = vec3(-1, -1,  1);
    const vec3 v3 = vec3( 1, -1, -1);
    const vec3 v4 = vec3(-1,  1, -1);

    for(int n = 0; n < 10; n++) 
    {
        if(p.x + p.y < 0) p.xy = -p.yx;
        if(p.x + p.z < 0) p.xz = -p.zx;
        if(p.y + p.z < 0) p.zy = -p.yz;
        
        p = p * Scale - v1 * (Scale - 1.0);
    }
   
    return sdBox(p, vec3(1.0,1.0,1.0)) * pow(Scale, -10.0) * size;
}

float sdOctahedron(vec3 p, float s)
{
    p = abs(p);
    float m = p.x + p.y + p.z - s;
    vec3 r = 3.0*p - m;
	vec3 q;

    if( r.x < 0.0 ) q = p.xyz;
    else if( r.y < 0.0 ) q = p.yzx;
    else if( r.z < 0.0 ) q = p.zxy;
    else return m*0.57735027;

    float k = clamp(0.5*(q.z-q.y+s),0.0,s); 
    return length(vec3(q.x,q.y-s+k,q.z-k)); 
}

float sdCone( vec3 p, float h )
{
    const vec2 c = vec2(sin(radians(60.0)), cos(radians(60.0)));
    float q = length(p.xz);
    return max(dot(c.xy,vec2(q,p.y)),-h-p.y);
}

float intersectAABB(vec3 ro, vec3 rd, vec3 bMin, vec3 bMax) {
    vec3 invRd = 1.0 / rd;
    vec3 t0 = (bMin - ro) * invRd;
    vec3 t1 = (bMax - ro) * invRd;
    vec3 tmin = min(t0, t1);
    vec3 tmax = max(t0, t1);
    float fmin = max(max(tmin.x, tmin.y), tmin.z);
    float fmax = min(min(tmax.x, tmax.y), tmax.z);
    return (fmax >= max(fmin, 0.0)) ? fmin : 1e9;
}

float checkDistToObj(vec3 p, int id)
{
    float dist = 1e9;

    Object obj = objects[id];
    vec3 localP = p - obj.pos.xyz;
    mat3 rot = mat3(obj.rotMatrix); 
    vec3 q = localP * rot;
    int type = int(obj.type.x);

    float bound = length(q) - (obj.size.x * 2.5); 
    if (type == 4) 
    {
        if (bound > 0.5) 
        {
            dist = bound; 
        } 
        else 
        {
            dist = sdMandelbulb(q / obj.size.x) * obj.size.x;
        }
    } 
    else 
    {
        if (type == 0) dist = sdSphere(q, obj.size.x);
        else if (type == 1) dist = sdBox(q, obj.size.xyz);
        else if (type == 2) dist = sdTorus(q, obj.size.xy);
        else if (type == 3) dist = sdHexagon(q, obj.size.xy);
        else if (type == 5) dist = sdMengerSponge(q, obj.size.xyz);
        else if (type == 6) dist = sdSierpinskiTetrahedron(q, obj.size.x);
        else if (type == 7) dist = sdOctahedron(q, obj.size.x);
        else if (type == 8) dist = sdCone(q, obj.size.x);
        else dist = 1e9;
    }

    return dist;
}

Hit checkScene(vec3 ro, vec3 rd)
{
    Hit res;
    res.dist = 1e9;
    res.id   = -1;

    int stack[32]; 
    int ptr = 0;
    stack[ptr++] = 0;

    for(int safety = 0; safety < 1000; safety++) 
    {
        if (ptr <= 0) break;
        
        int idx = stack[--ptr];
        BVHNode node = nodes[idx];

        if (intersectAABB(ro, rd, node.bMinW.xyz, node.bMaxW.xyz) > res.dist) continue;

        int left = int(node.bMinW.w);
        if (left < 0) 
        {
            int objId = -(left + 1);
            float d = checkDistToObj(ro, objId); 
            if (d < res.dist) { res.dist = d; res.id = objId; }
        } 
        else 
        {
            stack[ptr++] = int(node.bMaxW.w);
            stack[ptr++] = left;              
        }
    }
    return res;
}

vec3 calcObjNormal(vec3 p, int id) 
{
    vec2 e = vec2(1.0,-1.0)*0.5773;
    const float eps = 0.0005;
    return normalize
    ( 
        e.xyy*checkDistToObj( p + e.xyy*eps, id) + 
        e.yyx*checkDistToObj( p + e.yyx*eps, id) + 
        e.yxy*checkDistToObj( p + e.yxy*eps, id) + 
        e.xxx*checkDistToObj( p + e.xxx*eps, id) 
    );
}

float getSceneSDF(vec3 p, int skipId) 
{
    float minDist = 1e9;

    if (skipId != -1) 
    {
        minDist = checkDistToObj(p, skipId);
    }

    int stack[24]; 
    int ptr = 0;
    stack[ptr++] = 0;

    while(ptr > 0) 
    {
        int idx = stack[--ptr];
        BVHNode node = nodes[idx];

        vec3 center = (node.bMinW.xyz + node.bMaxW.xyz) * 0.5;
        vec3 halfSize = (node.bMaxW.xyz - node.bMinW.xyz) * 0.5;

        float dBox = sdBoxFast(p - center, halfSize);
        if (dBox > minDist) continue; 

        int left = int(node.bMinW.w);
        if (left < 0) 
        {
            int objId = -(left + 1);
            if (objId != skipId) 
            {
                minDist = min(minDist, checkDistToObj(p, objId));
            }
        } 
        else 
        {
            stack[ptr++] = int(node.bMaxW.w);
            stack[ptr++] = left;
        }
    }
    return minDist;
}

float calcShadow(vec3 ro, vec3 rd, float mint, float maxt, float k, int hitId)
{
    float res = 1.0;
    float t = mint;
    for(int i = 0; i < 24; i++)
    {
        float h = getSceneSDF(ro + rd * t, hitId);
        if(h < 0.001) return 0.0;
        
        res = min(res, k * h / t);
        
        t += clamp(h, 0.01, 0.5); 
        if(t > maxt) break;
    }
    return clamp(res, 0.0, 1.0);
}

void main()
{
	vec2 fragCoord = gl_FragCoord.xy;
	vec2 uv = (gl_FragCoord.xy / camResolution) * 2.0 - 1.0;
    uv.x *= camResolution.x / camResolution.y;

	vec3 rayOrigin = camPos;
	vec3 rayDirect = normalize(camFront + uv.x * camRight + uv.y * camUp);

    float t = 0.0;
    Hit hit;
    for (int i = 0; i < 96; i++) 
    {
        vec3 p = rayOrigin + rayDirect * t;
        hit = checkScene(p, rayDirect);
        
        if (hit.dist < 0.001) break;
        t += hit.dist;
        
        if (t > 100.0) break;
    }
    vec3 col = vec3(0.03);

    if (t < 100.0 && hit.id >= 0) 
    {
        vec3 p = rayOrigin + rayDirect * t;
        Object obj = objects[hit.id];
        
        vec3 n = calcObjNormal(p, hit.id);

        vec3 lDir = normalize(globalLightDir);
        float diff = max(dot(n, lDir), 0.0) + 0.1;

        float shadow = calcShadow(p + n * 0.01, lDir, 0.01, 10.0, 16.0, hit.id);
        float amb = 0.15;

        float lighting = amb + diff * shadow; 
        col = obj.color.xyz * lighting;
        
    }

    FragColor = vec4(pow(col, vec3(1.0/2.2)), 1.0);
}