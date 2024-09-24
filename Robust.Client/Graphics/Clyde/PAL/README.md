# PAL: How And Why Not To Mess It Up

PAL is a "we have WebGPU at home" layer that avoids a stunning 90% of compat. and 100% native dependency issues that the WebGPU port might cause while also simultaneously improving Clyde's architecture.

This comes with a very big responsibility on the part of downstream developers:

_DO NOT RUIN PAL. LUNA ISN'T THE ONLY ONE WITH A SHED._

What does this mean?

* Keep the divide between Clyde and PAL separate.
    * _Use this exact mental model, and no other: Clyde is content that just happens to have privileged access for the sole reasons of method call performance and compatibility._
    * Due to legacy reasons, `IClyde` is still the formal platform interface. Functions which need to be there for compatibility have been kept for the moment but can be dropped _at any time._
* If it is possible for user content to cause a SIGSEGV, _that is a problem, because it implies an arbitrary read of CPU-side memory may exist._ Hazardous reads of GPU-side memory will be caught by the GPU (or if not by default, then with Robust Buffer Access enabled; the risk is certainly lower anyway).
    * A bug mid-way through PAL's development was a crash on start due to an EBO not being bound. This case has since been caught with an explicit runtime check (even on debug). Here's how you'd exploit it:
        1. Set the index offset to a value such that you've gone from pointing to a null pointer to pointing at an object which Robust wants to keep hidden from you.
        2. Draw points/lines/whatever using this index data to a framebuffer, which you read from. Assume you have some shader which turns this into useful information.
        3. You now have a memory read oracle, which, assuming you have some way to get object addresses, may let you read, say, the user's auth details.
    * Suffice to say, treat these bugs seriously.

Cheers, ~ 20kdc
