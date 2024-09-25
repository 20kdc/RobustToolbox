// Vertex position.
/*layout (location = 0)*/ attribute vec2 aPos;
// Texture coordinates.
/*layout (location = 1)*/ attribute vec2 tCoord;
/*layout (location = 2)*/ attribute vec2 tCoord2;
// Colour modulation.
/*layout (location = 3)*/ attribute vec4 modulate;

varying vec2 UV;
varying vec2 UV2;
varying vec2 Pos;
varying vec4 VtxModulate;

// Maybe we should merge these CPU side.
// idk yet.
uniform mat3 modelMatrix;

// [SHADER_HEADER_CODE]

void main()
{
    vec3 transformed = projectionMatrix * viewMatrix * modelMatrix * vec3(aPos, 1.0);
    vec2 VERTEX = transformed.xy;

    // [SHADER_CODE]

    // Pixel snapping to avoid sampling issues on nvidia.
    VERTEX += 1.0;
    VERTEX /= SCREEN_PIXEL_SIZE*2.0;
    VERTEX = floor(VERTEX + 0.5);
    VERTEX *= SCREEN_PIXEL_SIZE*2.0;
    VERTEX -= 1.0;

    gl_Position = vec4(VERTEX, 0.0, 1.0);
    Pos = (VERTEX + 1.0) / 2.0;
    UV = tCoord;
    UV2 = tCoord2;
    VtxModulate = zFromSrgb(modulate);
}
