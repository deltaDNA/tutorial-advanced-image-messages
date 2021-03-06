﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using DeltaDNA;
using System;

public class Tutorial : MonoBehaviour
{
    int userLevel = 1;
    public Text txtUserLevel ;

    // Start is called before the first frame update
    void Start()
    {
        txtUserLevel.text = string.Format("Level : {0}", userLevel);

        // Hook up callback to fire when DDNA SDK has received session config info, including Event Triggered campaigns.
        DDNA.Instance.NotifyOnSessionConfigured(true);
        DDNA.Instance.OnSessionConfigured += (bool cachedConfig) => GetGameConfig(cachedConfig);

        // Allow multiple game parameter actions callbacks from a single event trigger        
        DDNA.Instance.Settings.MultipleActionsForEventTriggerEnabled = true;

        // SDK slightly modified to display multiple Image Messages on a single event trigger
        // JIRA- DELTADNA-230
        // Ln-197 of Helpers\Settings.cs added additional property
        // Ln-91 of Triggers\EventAction additional condition added 
        DDNA.Instance.Settings.MultipleActionsForImageMessagesEnabled = true;

        //Register default handlers for event triggered campaigns. These will be candidates for handling ANY Event-Triggered Campaigns. 
        //Any handlers added to RegisterEvent() calls with the .Add method will be evaluated before these default handlers. 
        DDNA.Instance.Settings.DefaultImageMessageHandler =
            new ImageMessageHandler(DDNA.Instance, imageMessage => {
                // do something with the image message
                myImageMessageHandler(imageMessage);
            });
        DDNA.Instance.Settings.DefaultGameParameterHandler = new GameParametersHandler(gameParameters => {
            // do something with the game parameters
            myGameParameterHandler(gameParameters);
        });        
        DDNA.Instance.SetLoggingLevel(DeltaDNA.Logger.Level.DEBUG);
        DDNA.Instance.StartSDK();


        // Update UI to reflect Event Sending State
        Text txtEventTracking = GameObject.Find("txtEventRecording").GetComponent<Text>();
        string trackingState = PlayerPrefs.HasKey(DDNA.PF_KEY_STOP_TRACKING_ME) ? "DISABLED" : "ENABLED";
        txtEventTracking.text = "Events Recording " + trackingState;
    }


    // The callback indicating that the deltaDNA has downloaded its session configuration, including 
    // Event Triggered Campaign actions and logic, is used to record a "sdkConfigured" event 
    // that can be used provision remotely configured parameters. 
    // i.e. deferring the game session config until it knows it has received any info it might need
    public void GetGameConfig(bool cachedConfig)
    {
        Debug.Log("Configuration Loaded, Cached =  " + cachedConfig.ToString());
        Debug.Log("Recording a sdkConfigured event for Event Triggered Campaign to react to");

        // Create an sdkConfigured event object
        var gameEvent = new GameEvent("sdkConfigured")
            .AddParam("clientVersion", DDNA.Instance.ClientVersion)
            .AddParam("userLevel", userLevel);

        // Record sdkConfigured event and run default response hander
        DDNA.Instance.RecordEvent(gameEvent).Run();
    }



    private void myGameParameterHandler(Dictionary<string, object> gameParameters)
    {
        // Parameters Received      
        Debug.Log("Received game parameters from event trigger: " + DeltaDNA.MiniJSON.Json.Serialize(gameParameters));

        if (gameParameters != null)
        {
            
            if (gameParameters.ContainsKey("personalEventWhitelist"))
            {
                // The Personal Event Whitelist limits events the player can send during this session.
                // It relies on some SDK modifications described in JIRA ticket 
                var dict = DeltaDNA.MiniJSON.Json.Deserialize(gameParameters["personalEventWhitelist"].ToString()) as Dictionary<string, object>;

                if (dict.ContainsKey("events"))
                {
                    Debug.Log("Player restricted to sending only the following events " + DeltaDNA.MiniJSON.Json.Serialize(dict["events"])); 
                    List<string> personalEventsWhitelis = new List<string>();

                    foreach (object s in (List<object>)dict["events"])
                    {
                        personalEventsWhitelis.Add(s.ToString());
                    }
                    DDNA.Instance.SetPlayerSessionEvents(personalEventsWhitelis);
                }
            } else if (gameParameters.ContainsKey("eventsDisabled") && Convert.ToInt32(gameParameters["eventsDisabled"]) == 1)
            {
                // Key added to game to Stop tracking player based on GDPR functionality from the next session onwards
                // Can be reversed by deleting this key. Can also be achieved with DDNA.Instance.StopTrackingMe();                
                PlayerPrefs.SetInt(DDNA.PF_KEY_STOP_TRACKING_ME, 1);
                Debug.Log("Stop Tracking Player next Session");                
            }
        }


    }


    private void myImageMessageHandler(ImageMessage imageMessage)
    {
        // Add a handler for the 'dismiss' action.
        imageMessage.OnDismiss += (ImageMessage.EventArgs obj) => {

            Debug.Log("Image Message dismissed by " + obj.ID);
            // NB : parameters not processed if player dismisses action
        };

        // Add a handler for the 'action' action.
        imageMessage.OnAction += (ImageMessage.EventArgs obj) => {
            Debug.Log("Image Message actioned by " + obj.ID + " with command " + obj.ActionValue);

            // Process parameters on image message if player triggers image message action
            if (imageMessage.Parameters != null) myGameParameterHandler(imageMessage.Parameters);
        };

        imageMessage.OnDidReceiveResources += () =>
        {
            Debug.Log("Received Image Message Assets");
        };


        // the image message is already cached and prepared so it will show instantly
        imageMessage.Show();

        // This Image Message Contains contains custom JSON to extend capabilities of contents        
        if (imageMessage.Parameters.ContainsKey("actionExtension"))
        {
            // DynamicImageMessage dm = new DynamicImageMessage(imageMessage, this.transform);           
            DynamicImageMessage dyn = gameObject.AddComponent<DynamicImageMessage>();
            dyn.SetImageMessage(imageMessage);

            DateTime dt = DateTime.Now.AddSeconds(720);
            dyn.SetExpiryTime(dt);
        }

    }




    //  Generic Method to make an Engage In-Game Decision Point campaign request for an Image Message
    private void DecisionPointImageMessageCampaignRequest(string decisionPointName, Params decisionPointParmeters)
    {

        DDNA.Instance.EngageFactory.RequestImageMessage(decisionPointName, decisionPointParmeters, (imageMessage) => {

            // Check we got an engagement with a valid image message.
            if (imageMessage != null)
            {


                // Process Multiple PopUps from decision point campaign
                ProcessMultiplePopups(imageMessage.Parameters);

                Debug.Log("Engage Decision Point returned a valid image message.");
                myImageMessageHandler(imageMessage);
            }
            else
            {
                Debug.Log("Engage Decision Point didn't return an image message.");
            }
        });
    }

    private void ProcessMultiplePopups(Dictionary<string, object> gameParameters)
    {       
        // Check for "campaignList" game Parameter specifying follow on campaigns. 
        if (gameParameters.ContainsKey("campaignList"))
        {
            CampaignList campaignList = new CampaignList();
            campaignList.LoadCampaigns(gameParameters["campaignList"].ToString());

            // Make additional Decision Poing Campaign requests for each campaign in campaignList
            foreach(Campaign campaign in campaignList.campaigns)
            {
                Params dpRequestParams = new Params().AddParam("campaign", campaign.campaign);
                DecisionPointImageMessageCampaignRequest(campaign.decisionPoint, dpRequestParams);
            }
        }
    }



    // UI Button Handlers
    public void BttnLevelUp_Clicked()
    {
        userLevel++;
        txtUserLevel.text = string.Format("Level : {0}", userLevel);

        GameEvent myEvent = new GameEvent("levelUp")
            .AddParam("userLevel", userLevel)
            .AddParam("levelUpName", string.Format("Level {0}", userLevel));
        
        DDNA.Instance.RecordEvent(myEvent).Run();

    }


    // Multi Popup Tests
    public void BttnDecisionPointTest_Clicked()
    { 
        DecisionPointImageMessageCampaignRequest("test", null);
    }
    public void BttnEventTriggeredTest_Clicked()
    {

        GameEvent myEvent = new GameEvent("multiTest");
        DDNA.Instance.RecordEvent(myEvent).Run();
    }
   
    // Restart Player Tracking next session
    public void BttnStartTrackingMe()
    {
        PlayerPrefs.DeleteKey(DDNA.PF_KEY_STOP_TRACKING_ME);       
        Debug.Log("Start tracking me again next session");
    }
    

}

