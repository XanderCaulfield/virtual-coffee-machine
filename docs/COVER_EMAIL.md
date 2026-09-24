# Cover email draft

> Paste into the submission email. Written as Xander, 3–4 lines.

Hi team,

Here is my take-home for the .NET role: the **Virtual Coffee Machine**, a
Blazor WebAssembly vending machine that takes Australian coins, rejects 1¢/2¢,
brews three coffees and returns change broken into coinage. It's live at
**https://virtual-coffee-machine.onrender.com** (free tier, so it takes ~1 min
to wake up — the README has a GIF while you wait), with the code, 986 tests and
CI at **https://github.com/XanderCaulfield/virtual-coffee-machine**. The part
I'm proudest of is the change algorithm: greedy over the canonical AUD
denominations, exhaustively proven minimal against a dynamic-programming
reference for every payable amount — the kind of small decision I enjoy getting
provably right. Thanks for your time!

— Xander
