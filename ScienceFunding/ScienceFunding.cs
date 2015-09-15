﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Xml.Serialization;
using System.Linq;
using System.Text;

namespace ScienceFunding
{
    /// <summary>
    /// Used to store the essential data regarding a science report when it's received.
    /// </summary>
    public struct ScienceReport
    {
        public float funds;
        public float rep;
        public string subject;

        public ScienceReport(float funds, float reputation, string subject)
        {
            this.funds = funds;
            this.rep = reputation;
            this.subject = subject;
        }

        /// <summary>
        /// Converts the report to a string with this format:
        /// "Subject of the experiment: 666.0 funds, 777.0 rep." 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return this.subject + ": " +
                   this.funds.ToString("F1") + " funds, " +
                   this.rep.ToString("F1") + " rep.";
        }

        /// <summary>
        /// Converts the object to a ConfigNode for saving
        /// </summary>
        public ConfigNode ToConfigNode()
        {
            ConfigNode retval = new ConfigNode("REPORT");
            retval.AddValue("funds", this.funds);
            retval.AddValue("rep", this.rep);
            retval.AddValue("subject", this.subject);

            return retval;
        }

        /// <summary>
        /// Parses a ConfigNode into a report.
        /// </summary>
        public static ScienceReport FromConfigNode(ConfigNode node)
        {
            return new ScienceReport(
                funds: float.Parse(node.GetValue("funds")),
                reputation: float.Parse(node.GetValue("rep")),
                subject: node.GetValue("subject")
            );
        }
    }

    /// <summary>
    /// Main logic of the mod.
    /// It's just a very very simple class that listens the OnScienceReceived game event,
    /// and awards the player with the correspoding rewards.
    /// The configuration is parsed at every scene, so it's very easy to reconfigure on-the-fly
    /// </summary>
    public class ScienceFunding : ScenarioModule
    {
        // Award (fundsMult * sciencePoints) funds for every report
        public float fundsMult;

        // Award (repMult * sciencePoints) reputation points for every report
        public float repMult;

        // Generate a notification every queueLength messages
        public int queueLength = 5;
        public Queue<ScienceReport> queue;

        /// <summary>
        /// "Constructor": it's not really the constructor, but Unity calls it
        /// immediately after the constructor, so...
        /// </summary>
        public void Awake()
        {
            // Start listening on the event to award the rewards
            GameEvents.OnScienceRecieved.Add(ScienceReceivedHandler);
            ScienceFunding.Log("listening for science...");
        }

        /// <summary>
        /// Load the saved queue.
        /// </summary>
        public override void OnLoad(ConfigNode node)
        {
            LoadConfiguration();

            this.queue = new Queue<ScienceReport>(this.queueLength);
            if (node.HasNode("QUEUE"))
            {
                // If there's a stored queue, try to parse each element to repopulate the message queue.
                foreach (ConfigNode reportNode in node.GetNodes("REPORT"))
                {
                    try
                    {
                        this.queue.Enqueue(ScienceReport.FromConfigNode(reportNode));
                    }
                    catch (Exception)
                    {
                        ScienceFunding.Log("Bad value found in queue, skipping:\n" + reportNode.ToString());
                        continue;
                    }
                }
            }
            else
            {
                ScienceFunding.Log("No node to load");
                node.AddNode(new ConfigNode("QUEUE"));
            }

            ScienceFunding.Log("Loaded " + this.queue.Count.ToString() + " records");
        }

        /// <summary>
        /// Science transmission handler: computes the funds and reputation boni
        /// and awards them to the player.
        /// </summary>
        public void ScienceReceivedHandler(float science, ScienceSubject sub, ProtoVessel v, bool whoKnows)
        {
            ScienceFunding.Log("Received " + science + " science points");

            // Don't bother for zero science
            if (science == 0)
                return;

            float funds = science * this.fundsMult;
            float rep = science * this.repMult;

            // Cannot award funds if it's not a career
            if (Funding.Instance != null)
            {
                Funding.Instance.AddFunds(funds, TransactionReasons.ScienceTransmission);
                ScienceFunding.Log("Added " + funds + " funds");
            }

            // Cannot award reputation in sandbox
            if (Reputation.Instance != null)
            {
                Reputation.Instance.AddReputation(rep, TransactionReasons.ScienceTransmission);
                ScienceFunding.Log("Added " + rep + " reputation");
            }

            // Add the new report to the queue, and also send the notification if the queue has reached its limit.
            this.queue.Enqueue(new ScienceReport(funds, rep, sub.title));
            if (this.queue.Count > this.queueLength)
                SendReport();
        }

        /// <summary>
        /// Empties the queue and sends the notification to the player.
        /// </summary>
        private void SendReport()
        {
            ScienceFunding.Log("Posting the user notification");

            // I don't even know how this might happen, honestly.
            // But it's almost midnight and I get paranoid when I program late at night.
            if (this.queue.Count == 0)
                return;

            // Header
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Your recent research efforts have granted you the following rewards:");
            builder.AppendLine("");

            // Append a string for every report in the queue
            ScienceReport cursor;
            while (this.queue.Count > 0)
            {
                cursor = this.queue.Dequeue();
                builder.AppendLine(cursor.ToString());
            }

            PostMessage(
                "New funds available!",
                builder.ToString(),
                MessageSystemButton.MessageButtonColor.BLUE,
                MessageSystemButton.ButtonIcons.MESSAGE
            );
        }

        /// <summary>
        /// Saves the current message queue to the persistence file.
        /// This is necessary because the plugin's instance is destroyed between scenes.
        /// A static member might do the same thing, but it only works during one session
        /// while saving to the persistence file ensures that you don't screw up
        /// if the player reloads a previous save.
        /// </summary>
        /// <param name="node"></param>
        public override void OnSave(ConfigNode node)
        {
            ScienceFunding.Log("Saving message queue");

            // Ensure a fresh save each time
            if (node.HasNode("QUEUE"))
                node.RemoveNode("QUEUE");
            ConfigNode queueNode = new ConfigNode("QUEUE");
            node.AddNode(queueNode);

            foreach (ScienceReport report in this.queue)
            {
                queueNode.AddNode(report.ToConfigNode());
            }

            ScienceFunding.Log("Saved " + this.queue.Count.ToString() + " records");
        }

        public void OnDestroy()
        {
            GameEvents.OnScienceRecieved.Remove(ScienceReceivedHandler);
            ScienceFunding.Log("OnDestroy, removing handler.");
        }

        #region Utilities

        /// <summary>
        /// Loads the configuration file.
        /// </summary>
        public void LoadConfiguration()
        {
            ConfigNode node = GetConfig();
            try
            {
                this.fundsMult = float.Parse(node.GetValue("funds"));
                this.repMult = float.Parse(node.GetValue("rep"));
                this.queueLength = int.Parse(node.GetValue("queueLength"));
            }
            catch (Exception e)
            {
                // Let's be honest: this try-catch is probably overkill, but on the other hand,
                // better safe than sorry I guess. If for some reason you can't parse the config values,
                // they are set to a totally-unbalanced default and the player is notified with a message.

                this.fundsMult = 1000;
                this.repMult = 1f;
                this.queueLength = 5;

                ScienceFunding.Log("There was an exception while loading the configuration: " + e.ToString());

                PostMessage(
                    "ScienceFunding error!",
                    "I'm sorry to break your immersion, but there seems to be an error in the configuration" +
                    " and ScienceFunding is not working properly right now. You should check the values in the config file.",
                    MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.ALERT
                );
            }

            ScienceFunding.Log("Configuration is " + this.fundsMult.ToString() + ", " + this.repMult.ToString() + ", " + this.queueLength.ToString());
        }

        /// <summary>
        /// Locates the settings file (right next to the assembly),
        /// reads it and parses it to a ConfigNode.
        /// </summary>
        static ConfigNode GetConfig()
        {
            string assemblyPath = Path.GetDirectoryName(typeof(ScienceFunding).Assembly.Location);
            string filePath = Path.Combine(assemblyPath, "settings.cfg");

            ScienceFunding.Log("Loading settings file:" + filePath);

            ConfigNode result = ConfigNode.Load(filePath).GetNode("SCIENCE_FUNDING_SETTINGS");
            ScienceFunding.Log(result.ToString());

            return result;
        }

        /// <summary>
        /// Quick wrapper to post a user notification with less hassle.
        /// </summary>
        static void PostMessage(string title,
                                string message,
                                MessageSystemButton.MessageButtonColor messageButtonColor,
                                MessageSystemButton.ButtonIcons buttonIcons)
        {
            MessageSystem.Message msg = new MessageSystem.Message(
                    title,
                    message,
                    messageButtonColor,
                    buttonIcons);
            MessageSystem.Instance.AddMessage(msg);
        }

        public static void Log(string msg)
        {
            Debug.Log("[ScienceFunding]: " + msg);
        }   

        #endregion      
    }
}
