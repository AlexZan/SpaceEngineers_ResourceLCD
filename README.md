# SpaceEngineers_ResourceLCD
Space Engineers programmable block script that shows sorted ore and ingot totals on separate LCDs with aligned columns.

## Panel setup

Add the following tags to LCD panels you want the script to manage:

* `[ResLCD Ore]` – displays the ore summary on every tagged panel.
* `[ResLCD Ingot]` – displays the ingot summary on every tagged panel.

You can place the tag in the panel's **Edit text** field or in its **Custom Data**. The script will copy the tag into Custom Data so panels stay registered after the script updates their display.
