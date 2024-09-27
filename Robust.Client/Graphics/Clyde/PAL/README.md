# PAL: How And Why Not To Mess It Up

PAL is a "we have WebGPU at home" layer that avoids a stunning 90% of compat. and 100% native dependency issues that the WebGPU port might cause while also simultaneously improving Clyde's architecture.

This comes with a very big responsibility on the part of downstream developers:

_DO NOT RUIN PAL. LUNA ISN'T THE ONLY ONE WITH A SHED._

What does this mean?

* Keep the divide between Clyde and PAL separate.
    * _Use this exact mental model, and no other: Clyde is content that just happens to have privileged access for the sole reasons of method call performance and compatibility._
    * Due to legacy reasons, `IClyde` still retains platform interface functionality. Functions which need to be there for compatibility have been kept for the moment but can be dropped _at any time._
* If it is possible for user content to cause a SIGSEGV, _that is a problem, because it implies an arbitrary read of CPU-side memory may exist._ Hazardous reads of GPU-side memory will be caught by the GPU (or if not by default, then with Robust Buffer Access enabled; the risk is certainly lower anyway).
    * A bug mid-way through PAL's development was a crash on start due to an EBO not being bound. This case has since been caught with an explicit runtime check (even on debug). Here's how you'd exploit it:
        1. Set the index offset to a value such that you've gone from pointing to a null pointer to pointing at an object which Robust wants to keep hidden from you.
        2. Draw points/lines/whatever using this index data to a framebuffer, which you read from. Assume you have some shader which turns this into useful information.
        3. You now have a memory read oracle, which, assuming you have some way to get object addresses, may let you read, say, the user's auth details.
    * Suffice to say, treat these bugs seriously.

Cheers, ~ 20kdc

# PAL design decision rationale

## Render States

The entire render states mechanism is a concession to three things:

* The need to actually have a working Clyde (aka the reason WebGPU hasn't happened yet).
    * Clyde heavily relies on a stateful GL to function. Replacing it with a stateless layer would just mean emulating a stateful GL that we then run on a stateless layer which we then run on a stateful GL again.
* Ergonomics.
    * Take a look at how much state there is in `PAL.DrawCall.cs`... I originally wanted to use a `struct GPUDrawCall` (which the file is named after)... changed my mind.
    * Now remember you're going to be expected to specify this with every draw call. Like, it's doable, especially with sensible defaults and `new {}` initialization, _but..._
* Performance.
    * Basically, if a render state is in active use, anything we _can_ keep up to date in the GL, we do.
        * What this means _in practice_ is a ~1:1 RS update / GL update ratio for the draw call "meat".
        * The actual function that performs a draw call only needs to verify that the state is bound.
            * There is _something_ of an exception in the form of the UBO emulator, but this is an obvious special case.
        * While there is a penalty for rebinding states, this should happen _extremely_ rarely (_if Content doesn't use PAL, once per frame, and even this is part of the safety reset logic from Clyde_), so long sequences of draw calls with the same state object (like, say, the entire Clyde batcher) remain optimized.

## UBO Emulator

The UBO emulator is used to emulate uniform buffers more or less perfectly and optimally on GLES2.

I'd go as far as to argue that if you would _otherwise_ be setting uniforms with each draw call, __it is almost certainly faster, even on GLES2, to use the UBO emulator.__

The UBO emulator is based on each shader program carrying a dictionary of uniform block indices and versions.

_When a draw call is performed,_ the intersection of uniform block indices between the render state and shader program are checked for version mismatches. The mismatching virtual uniform blocks are updated.

That's the entire mechanism, but I don't think I've explained anywhere else _why_ this is the right way to do this, so here goes:

* We _can't_ keep emulated UBO data constantly up to date like other state.
    * _If we do that, we create correctness issues when someone installs a UBO not actually meant for this shader. **This is extremely bad for the API user!**_
* The pre-PAL mechanism is basically the current mechanism but more manual... _and without the version checking scheme._ It worked for inside Clyde, but it's not scalable, at all.
    * The pre-PAL UBO emulator was built to get SS14 working on GLES2 at any cost, since that's what device I had at the time could do. It was literally "you use this or you use llvmpipe".
    * Giving API users this much power comes at a compat. risk. Even the current iteration is probably bad for GLES2 support. With that said, I don't think the devices that can only handle GLES2 have good enough CPUs to run SS14 anymore.
