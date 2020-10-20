
varying mediump vec2 fragRefPos;
// x: actual angle, y: horizontal (1) / vertical (-1)
varying mediump vec2 fragAngle;

void main()
{
    // Stuff that needs to be inferred to avoid interpolation issues.
    mediump vec2 rayNormal = vec2(cos(fragAngle.x), -sin(fragAngle.x));

    // Depth calculation accounting for interpolation.
    mediump float dist;

	// Get rid of sparklies.
	mediump vec2 fragRefPosMod = fragRefPos - (sign(fragRefPos) * (1.0 / 32.0));

    if (fragAngle.y > 0.0) {
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

