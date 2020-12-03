// Secondary implementation of shadow program for stencil mode.

// xy: A, zw: B
attribute vec4 aPos;
// x: deflection(0=A/1=B) y: height
attribute vec2 subVertex;

// Note: This is *not* the standard projectionMatrix!
uniform vec2 shadowLightCentre;

void main()
{
    vec2 pt = mix(aPos.zw, aPos.xy, subVertex.x);
    // Make relative to light
    pt -= shadowLightCentre;
    // Multiply
    pt = pt * ((subVertex.y * 255.0) + 1.0);
    // Back to normal
    pt += shadowLightCentre;
    vec3 transformed = projectionMatrix * viewMatrix * vec3(pt, 1.0);
    gl_Position = vec4(transformed.xy, 0.0, 1.0);
}

