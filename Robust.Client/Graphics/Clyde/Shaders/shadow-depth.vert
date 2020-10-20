// Polar-coordinate mapper.
// While inspired by https://www.gamasutra.com/blogs/RobWare/20180226/313491/Fast_2D_shadows_in_Unity_using_1D_shadow_mapping.php ,
//  has one major difference:
// The assumption here is that the shadow sampling must be reduced.
// The original cardinal-direction mapper as written by PJB used 4 separate views.
// As such, it's still an increase in performance to only render 2 views.
// And as such, a line can be split across the 2 views.

// xy: A, zw: B
attribute vec4 aPos;
// x: deflection(0=A/1=B) y: height
attribute vec2 subVertex;

// xy: actual ref pos, z: horizontal (1) / vertical (-1)
varying vec3 fragRefPos;

// Note: This is *not* the standard projectionMatrix!
uniform vec2 shadowLightCentre;

uniform float shadowOverlapSide;

void main()
{
    // aPos is clockwise, but we need anticlockwise so swap it here
    vec2 pA = aPos.zw - shadowLightCentre;
    vec2 pB = aPos.xy - shadowLightCentre;
    float xA = atan(pA.y, -pA.x);
    float xB = atan(pB.y, -pB.x);

    // We need to reliably detect a clip, as opposed to, say, a backdrawn face.
    // So a clip is when the angular area is >= 180 degrees (which is not possible with a quad and always occurs when wrapping)
    if (abs(xA - xB) >= PI)
    {
        // Oh no! It clipped...

        // If such that xA is on the right side and xB is on the left:
        //  Pass 1: Adjust left boundary past left edge
        //  Pass 2: Adjust right boundary past right edge

        // If such that xA is on the left side and xB is on the right!
        //  Pass 1: Adjust left boundary past right edge
        //  Pass 2: Adjust right boundary past left edge

        if (shadowOverlapSide < 0.5)
        {
            // ...and we're adjusting the left edge...
            xA += sign(xB - xA) * PI * 2.0;
        }
        else
        {
            // ...and we're adjusting the right edge...
            xB += sign(xA - xB) * PI * 2.0;
        }
    }

    // Depth divide MUST be implemented here no matter what,
    //  because GLES SL 1.00 doesn't have gl_FragDepth.
    // Keep in mind: Ultimately, this doesn't matter, because we use the colour buffer for actual casting,
    //  and we don't really need to have correction
    float depth = 1.0 - (1.0 / length(mix(pA, pB, subVertex.x)));

    // The new "double-sized triangle" layout implies that the horizontal part of the triangle also needs to be double-sized.
    // Note that there's a deliberate bias here to prevent holes appearing.
    float xBMod = xA + ((xB - xA) * 2.05);

    fragRefPos = vec3(pA, abs(pA.x - pB.x) - abs(pA.y - pB.y));

    gl_Position = vec4(mix(xA, xBMod, subVertex.x) / PI, mix(2.0, -2.0, subVertex.y), depth, 1.0);
}
