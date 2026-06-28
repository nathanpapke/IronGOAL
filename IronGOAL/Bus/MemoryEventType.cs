namespace IronGOAL.Bus;

public enum MemoryEventType
{
    Alloc,              // kmalloc succeeded.
    Free,               // kfree called.
    AllocFailed,        // kmalloc returned null - arena exhausted.
    ThresholdCrossed,   // Heap-used crossed a registered watermark.
    ArenaReset,         // Entire arena was cleared (e.g. on level unload).
    DmaTransfer         // dma-to-iop called — DMA transfer to IOP requested.
}
