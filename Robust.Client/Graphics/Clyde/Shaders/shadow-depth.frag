// xy: actual ref pos, z: horizontal (1) / vertical (-1)
varying mediump vec3 fragRefPos;

uniform highp float shadowCConvDiv;

void main()
{
    mediump float fragAngle = gl_FragCoord.x / shadowCConvDiv;

    // Stuff that needs to be inferred to avoid interpolation issues.
    mediump vec2 rayNormal = vec2(cos(fragAngle), -sin(fragAngle));

    // Depth calculation accounting for interpolation.
    highp float dist;

    mediump vec2 fragRefPosMod = fragRefPos.xy;

    // Try to reduce sparklies, maybe.
    if (gl_FrontFacing) {
        // front: lighting
        fragRefPosMod += sign(fragRefPosMod) / 32.0;
    } else {
        // back: solid fov
        fragRefPosMod -= sign(fragRefPosMod) / 32.0;
    }

    if (fragRefPos.z > 0.0) {
        // Line is horizontal
        dist = abs(fragRefPosMod.y / rayNormal.y);
    } else {
        // Line is vertical
        dist = abs(fragRefPosMod.x / rayNormal.x);
    }

    // Main body.
#ifdef HAS_DFDX
    mediump float dx = dFdx(dist);
    mediump float dy = dFdy(dist); // I'm aware derivative of y makes no sense here but oh well.
#else
    mediump float dx = 1.0;
    mediump float dy = 1.0;
#endif
    gl_FragColor = zClydeShadowDepthPack(vec2(dist, dist * dist + 0.25 * (dx*dx + dy*dy)));
}

