# Auto-off after X minutes standby for Wemo Insight switches

I got this idea after noticing that, even while spending the majority of its time on standby, my home theater was going to cost me ~$13/mo at home.

I know that the Wemo Insight keeps track of how long the connected load is "in standby" and thought it would be great if I could **turn off** the switch after a certain amount of time on standby to save some more money. [Turns out I'm not alone in this idea](http://community.wemo.com/t5/WEMO-Ideas/Auto-Off-WEMO-Insight-after-x-standby-minutes/idi-p/10932).

Well, we know [IFTTT is a great resource for automating your Wemo devices](https://ifttt.com/search/query/wemo), but unfortunately they don't provide such a trigger/action. So, it's time to build one!

## Step 1 - Set up the required IFTTT services
### Step 1.1 - Wemo Insight
Click [here](https://ifttt.com/services/wemo_insight_switch) and follow the steps to connect IFTTT to your Wemo account.
### Step 1.2 - Maker Webhooks 
Click [here](https://ifttt.com/services/maker_webhooks) and follow the steps to enable Maker connectivity.

Make note of the URL IFTTT provides. It should look something liked this:
`https://maker.ifttt.com/trigger/{event}/with/key/<key here>` - **save this for later**.

## Step 2 - Sign up for a _free_ Azure cloud account
Click [here](https://azure.microsoft.com/en-us/free/) to get started with a free account. The cool thing to note about what I've implemented is that it will _very likely_ **continue to be free** for as long as you use it due to the pricing nature of Azure Functions. (Check out how one very popular website runs on mere pennies/day [here](https://www.troyhunt.com/serverless-to-the-max-doing-big-things-for-small-dollars-with-cloudflare-workers-and-azure-functions/))
> Note: In order to sign up you'll have to have a Microsoft account. If you use outlook/hotmail/live.com you've already got one of these. You can create one with any e-mail address, though!

## Step 3 - Click this 'Deploy to Azure' button
This will take the resources I've created in this repo and push them out to your new Azure account. These are what do the meat of the work required for implementing the "wait for X minutes" logic we're missing in IFTTT using <a href="https://azure.microsoft.com/en-us/services/functions/" target="_blank">Azure Functions</a> and <a href="https://docs.microsoft.com/en-us/azure/azure-functions/durable-functions-overview" target="_blank">Azure Durable Functions</a>.

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://azuredeploy.net/)

This creates the "shell" in to which you'll be deploying code (in just one moment).

> **Note**: If you don't want to automatically get the updates that I push, you need to fork the repo and point to your own copy of it, putting the URL to the repo and branch you want to use in the deployment form then pull in updates as you see fit and deploy them to your fork. By default you will pull updates from this repo's `master` branch.

## Step 4 - Get the URL to your new Function

When the above is finished
1. Click the 'Manage' links that shows up:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/14/2018-11-14_15-03-47.png)

2. Click the item that has the the lightning bolt next to it and the name you put in to 'Site Name' in the deploy step:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/14/2018-11-14_15-04-31.png)

You should see a set of Functions shown, like this:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/14/9fd0061d.png)

Click on the `StandbyAutoOff` one, then choose `Get function URL`:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/14/2018-11-14_15-55-35.png)

Then click 'Copy':

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/14/2018-11-14_15-56-27.png)

Put this somewhere safe, you'll need it later.

## Step 5 - Connect IFTTT to Azure when your Wemo comes *out* of Standby
This is required due to the following scenario
* Wemo goes in to standby, you have timeout set to 20 minutes
* Wemo comes back out of standby at minute 5

You don't want the Wemo to shut off 15 minutes later. So we need to fire an event when the Wemo comes back out of standby as well.

### 5.1 - Create a new IFTTT Applet on your Insight switch
Create a new IFTTT Applet for your Insight switch, to be triggered when your switch **turns on**. To do this you use the `Switched on` trigger:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/ac3dde7f.png)

Choose the switch you want to monitor and click `Create trigger`.

For the `+that` portion, choose search for `Webhook` and choose the `Webhooks` recipe:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/68508afe.png)

then the `Make a web request` action:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/51788521.png)

* `URL`: The function URL you copied earlier
* `Method`: `POST`
* `Content-Type`: `application/json`
* `Body`: `{ "wemoId": "<unique name, NO SPACES>", "standbyEntered": false }`

> Example body: `{ "wemoId": "downstairstv", "standbyEntered": false }`

This tells our Azure Function that the device came out of standby, and it should stop the countdown to shutoff.

## Step 6 - Wire up IFTTT to shut off your Wemo switch when the Azure Function calls out to it
For this we use a Webhook *trigger* instead of action. Create a new Applet and search for webhook then choose `Webhooks` and then the `Receive a web request` action

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/da087e2c.png)

Give it a unique event name - something specific to the switch you'll be turning off - **without spaces** (e.g. `turnoff_downstairstv`) and click `Create trigger`. 

For the `+that` portion, configure shutting off the target Wemo Insight switch:

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/f9136fff.png)

## Step 7 - Connect IFTTT to Azure when your Wemo goes in to Standby
Repeat Step 5 above except choose `Standby mode entered` as the trigger

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/c5efed6f.png)

and for the `Body` of the webhook, make it look like this:

```
{
	"wemoId": "downstairstv",
	"standbyEntered": true,
	"timeoutDurationMinutes": 120,
	"callbackUrl": "https://maker.ifttt.com/trigger/<step 6 event name>/with/key/<key in URL from Step 1.2>"
}
```

where `callbackUrl` is the address you stored off from Step 1.2, configured with the event name you gave in Step 6. It should look something like this: `https://maker.ifttt.com/trigger/turnoff_downstairstv/with/key/asdfqwefbadrty`

**Important**: The `wemoId` property in this body **must** match the one you used for Step 5.

You can change the amount of time you wait to shut off the device by tweaking the value of `timeoutDurationMinutes`.

# How it works
The meat of the logic here is, obviously in the Azure Function. For the configuration here, the algorithm is as follows:
* When I get a request, check to see if I'm already wired up to listen for `wemoId`
  * If not, create a new listener for `wemoId` and let it know the value for `standbyEntered`
  * If so, just let it know the value that came in for `standbyEntered`
* When I get an alert on a new `standbyEntered` value
  * If it is `true`, start a timer, or ignore it if a timer is already going.
  * If it is `false`, the device turned back on so stop the timer and wait for another `standbyEntered` event later
* If I hit the timeout that was given to me for *the first `standbyEntered = true` alert for this `wemoId`*, send a message to the `callbackUrl`.

> **Important**: As highlighted, you can only set the timeout for a given Wemo switch once: the first time it's used. I hope to modify this solution in the future to accept changes to the timeout.

For IFTTT's part, we call the function with `standbyEntered = true` when the device goes in to standby, which starts (or restarts) the timer. Similarly, we call it with `standbyEntered = false` when the device turns on (comes out of standby).

## Important considerations
The alerts here revolve *entirely* around the configuration of your Wemo Insight switch. Most importantly the **On/Standby Threshold** setting. If you have this mis-configured, a few things are possible:
1. The Wemo will never trip as going in to Standby (your threshold is too low)
1. The Wemo will never trip as turning on (your threshold is too high)
1. The Wemo will oscillate constantly between On and Standby, overloading IFTTT and your Azure Function (your threshold is right on the edge and needs to be higher)

If you encounter any of these problems, have a look at your threshold setting.

When you initially set this up, I **recommend you have the IFTTT Applets set to send you notifications when they're triggered** until you've got things going the way you want. This is a great way to ensure things are set up properly. 

![](https://brandonhmsdnblog.blob.core.windows.net/images/2018/11/15/2018-11-14_16-32-06.png)