namespace IronGOAL.Bus;

public enum RenderCommandType
{
    DrawMesh,       // Submit a mesh for rendering this frame.
    SetTransform,   // Update an existing entity's world transform.
    SetMaterial,    // Swap the material bound to an entity.
    Clear           // Clear the render target (e.g. between scenes).
}
