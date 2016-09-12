/*
* Copyright (c) Microsoft Corporation. All rights reserved. This code released
* under the terms of the Microsoft Limited Public License (MS-LPL).
*/
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Controls;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Controls.Extensibility;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.Discussion.Client;
using System.Collections.Concurrent;

namespace Microsoft.TeamExplorerExtCodeReview.CodeReviewersInfo
{
    /// <summary>
    /// Selected file info section.
    /// </summary>
    [TeamExplorerSection(CodeReviewersInfoSection.SectionId, TeamExplorerPageIds.PendingChanges, 900)]
    public class CodeReviewersInfoSection : TeamExplorerBaseSection
    {
        #region Members

        public const string SectionId = "50948F36-9223-4E8C-A8A5-37A6225AE3E2";
        public VersionControlServer vcs = null;
        public WorkItemStore wis = null;
        public ConcurrentBag<CodeReviewComment> ReviewComments = new ConcurrentBag<CodeReviewComment>();
        public ConcurrentDictionary<string, Tuple<string, int, List<string>, List<string>>> DictOfChangedFileDetails = new ConcurrentDictionary<string, Tuple<string, int, List<string>, List<string>>>();
        #endregion

        /// <summary>
        /// Constructor.
        /// </summary>
        public CodeReviewersInfoSection()
            : base()
        {
            this.Title = "Recommended Code Reviewers";
            this.IsExpanded = true;
            this.IsBusy = false;
            this.SectionContent = new CodeReviewersInfoView();
            this.View.ParentSection = this;
        }
        /// <summary>
        /// Get the view.
        /// </summary>
        protected CodeReviewersInfoView View
        {
            get { return this.SectionContent as CodeReviewersInfoView; }
        }

        /// <summary>
        /// Initialize override.
        /// </summary>
        public override void Initialize(object sender, SectionInitializeEventArgs e)
        {
            base.Initialize(sender, e);

            // Find the Pending Changes extensibility service and sign up for
            // property change notifications
            IPendingChangesExt pcExt = this.GetService<IPendingChangesExt>();
            if (pcExt != null)
            {
                pcExt.PropertyChanged += pcExt_PropertyChanged;
            }

            ListOfRecommendations = new List<string>() { "... Loading recommendations ..." };
            ITeamFoundationContext context = this.CurrentContext;
            if (context != null && context.HasCollection)
            {
                vcs = context.TeamProjectCollection.GetService<VersionControlServer>();
                wis = context.TeamProjectCollection.GetService<WorkItemStore>();
            }

            GetReviewsAsync(context, pcExt.IncludedChanges);
        }

        public async void GetReviewsAsync(ITeamFoundationContext context, PendingChange[] changes)
        {
            //Get all code review responses

            await Task.Run(() =>
            {
                ConcurrentBag<CodeReviewComment> lstConcurrent = new ConcurrentBag<CodeReviewComment>();
                //http://stackoverflow.com/questions/16063271/using-tfs-api-how-can-i-find-the-comments-which-were-made-on-a-code-review
                var codeReviewRequests = wis.Query(@"SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Code Review Request' AND [System.CreatedDate] > '01/01/2016'");
                TeamFoundationDiscussionService service = new TeamFoundationDiscussionService();
                service.Initialize(context.TeamProjectCollection);
                IDiscussionManager discussionManager = service.CreateDiscussionManager();
                //List<CodeReviewComment> lstComm = new List<CodeReviewComment>();
                Parallel.For(0, codeReviewRequests.Count - 1,
                       index =>
                       {
                           //ReviewComments.AddRange(GetCodeReviewComments(discussionManager, codeReviewRequests[index].Id));
                           GetCodeReviewComments(discussionManager, codeReviewRequests[index].Id).ForEach((cr) =>
                           {
                               ReviewComments.Add(cr);
                           });
                       });


                //ReviewComments.AddRange(lstConcurrent);
                //get all the changed files in the pending changes list
                AssessRecommendedReviewers(context, changes);
            });
        }

        /// <summary>
        /// Dispose override.
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();

            IPendingChangesExt pcExt = this.GetService<IPendingChangesExt>();
            if (pcExt != null)
            {
                pcExt.PropertyChanged -= pcExt_PropertyChanged;
            }
        }

        /// <summary>
        /// Pending Changes Extensibility PropertyChanged event handler.
        /// </summary>
        private void pcExt_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "SelectedIncludedItems":
                    Refresh();
                    break;
            }
        }

        /// <summary>
        /// Refresh override.
        /// </summary>
        public async override void Refresh()
        {
            base.Refresh();
            await RefreshAsync();
        }

        /// <summary>
        /// Refresh the changeset data asynchronously.
        /// </summary>
        private async Task RefreshAsync()
        {
            try
            {
                // Set our busy flag and clear the previous data
                this.IsBusy = true;
                this.ServerPath = null;

                this.TotalNoOfChanges = 0;
                this.ListOfCommitters = new List<string>();
                this.ListOfReviewers = new List<string>();

                // Temp variables to hold the data as we retrieve it
                string serverPath = null;

                int totalnoofchanges = 0;
                List<string> listOfCommitters = new List<string>();
                List<string> listOfReviewers = new List<string>();

                // Grab the selected included item from the Pending Changes extensibility object
                PendingChangesItem selectedItem = null;
                IPendingChangesExt pcExt = GetService<IPendingChangesExt>();
                if (pcExt != null && pcExt.SelectedIncludedItems.Length > 0)
                {
                    selectedItem = pcExt.SelectedIncludedItems[0];
                }

                if (selectedItem != null && selectedItem.IsPendingChange && selectedItem.PendingChange != null)
                {
                    // Check for rename
                    if (selectedItem.PendingChange.IsRename && selectedItem.PendingChange.SourceServerItem != null)
                    {
                        serverPath = selectedItem.PendingChange.SourceServerItem;
                    }
                    else
                    {
                        serverPath = selectedItem.PendingChange.ServerItem;
                    }
                }
                else
                {
                    return;
                }
                                

                await Task.Run(() =>
                {
                    //check if its available in the Dict
                    if (!DictOfChangedFileDetails.ContainsKey(serverPath))
                    {
                        ITeamFoundationContext context = this.CurrentContext;
                        if (context != null && context.HasCollection)
                        {
                            vcs = context.TeamProjectCollection.GetService<VersionControlServer>();
                            wis = context.TeamProjectCollection.GetService<WorkItemStore>();
                            //get all changesets

                            IEnumerable<Changeset> history = vcs.QueryHistory(serverPath, RecursionType.Full, 50000);
                            totalnoofchanges = history.Count();
                            //Committers
                            foreach (var line in history.GroupBy(p => p.CommitterDisplayName/* + " (" + p.Committer + ")"*/).Select(group => new
                            {
                                Name = group.Key,
                                Count = group.Count()
                            })
                        .OrderByDescending(x => x.Count))
                            {
                                listOfCommitters.Add(line.Name + ": " + line.Count + " commit(s)");
                            }
                        }

                        //Reviewers
                        var tempFilteredList = ReviewComments.Where(rc => rc.ItemPath == serverPath);
                        foreach (var item in tempFilteredList.GroupBy(p => p.Author).Select(group => new
                        {
                            Name = group.Key,
                            Count = group.Count()
                        })
                        .OrderByDescending(x => x.Count))
                        {
                            listOfReviewers.Add(item.Name + ": " + item.Count + " review(s)");
                        }
                    }
                    else
                    {
                        totalnoofchanges = DictOfChangedFileDetails[serverPath].Item2;
                        listOfCommitters = DictOfChangedFileDetails[serverPath].Item3;
                        listOfReviewers = DictOfChangedFileDetails[serverPath].Item4;
                    }

                });
                //}

                // Now back on the UI thread, update the view data
                this.ServerPath = serverPath;

                this.TotalNoOfChanges = totalnoofchanges;
                this.ListOfCommitters = listOfCommitters;
                this.ListOfReviewers = listOfReviewers;
            }
            catch (Exception ex)
            {
                ShowNotification(ex.Message, NotificationType.Error);
            }
            finally
            {
                // Always clear our busy flag when done
                this.IsBusy = false;
            }
        }

        private List<CodeReviewComment> GetCodeReviewComments(IDiscussionManager dm, int workItemId)
        {
            List<CodeReviewComment> comments = new List<CodeReviewComment>();

            IAsyncResult result = dm.BeginQueryByCodeReviewRequest(workItemId, QueryStoreOptions.ServerOnly, new AsyncCallback(CallCompletedCallback), null);
            var output = dm.EndQueryByCodeReviewRequest(result);

            foreach (DiscussionThread thread in output.Where(o => o.RootComment != null && o.ItemPath != null))
            {

                comments.Add(new CodeReviewComment(thread.RootComment.Author.DisplayName, thread.ItemPath));
            }

            return comments;
        }
        private void CallCompletedCallback(IAsyncResult result)
        {
            // Handle error conditions here
        }


        /// <summary>
        /// Find the recommended reviewers for the files in pending changes
        /// </summary>
        private void AssessRecommendedReviewers(ITeamFoundationContext context, PendingChange[] changesList)
        {
            ConcurrentBag<RecommendedReviewers> ListOfRecommendedReviewers = new ConcurrentBag<RecommendedReviewers>();
            //foreach (var changedItem in changesList)
            Parallel.ForEach(changesList, (changedItem) =>
                {
                    string serverPath = String.Empty;
                    int totalnoofchanges = 0;
                    List<string> listOfCommitters = new List<string>();
                    List<string> listOfReviewers = new List<string>();

                    if (changedItem != null)
                    {
                    // Check for rename
                    if (changedItem.IsRename && changedItem.SourceServerItem != null)
                        {
                            serverPath = changedItem.SourceServerItem;
                        }
                        else
                        {
                            serverPath = changedItem.ServerItem;
                        }
                    }

                //await Task.Run(() =>
                //{
                //ITeamFoundationContext context = this.CurrentContext;
                if (context != null && context.HasCollection)
                    {
                        vcs = context.TeamProjectCollection.GetService<VersionControlServer>();
                        wis = context.TeamProjectCollection.GetService<WorkItemStore>();
                    //get all changesets

                    IEnumerable<Changeset> history = vcs.QueryHistory(serverPath, RecursionType.Full, 50000);
                        totalnoofchanges = history.Count();
                        DictOfChangedFileDetails[serverPath] = new Tuple<string, int, List<string>, List<string>>(serverPath, totalnoofchanges, new List<string>(), new List<string>());

                    //Committers
                    foreach (var line in history.GroupBy(p => p.CommitterDisplayName/* + " (" + p.Committer + ")"*/).Select(group => new
                        {
                            Name = group.Key,
                            Count = group.Count()
                        })
                            .OrderByDescending(x => x.Count))
                        {
                        //listOfCommitters.Add(line.Name + ": " + line.Count + " commit(s)");
                        DictOfChangedFileDetails[serverPath].Item3.Add(line.Name + ": " + line.Count + " commit(s)");

                            if (ListOfRecommendedReviewers.FirstOrDefault(f => f.Name == line.Name) == null)
                            {
                                ListOfRecommendedReviewers.Add(new RecommendedReviewers() { Name = line.Name, TotalNoOfCommits = line.Count, TotalNoOfReviews = 0 });
                            }
                            else
                            {
                                ListOfRecommendedReviewers.FirstOrDefault(f => f.Name == line.Name).TotalNoOfCommits += 1;
                            }
                        }


                    //Reviewers
                    var tempFilteredList = ReviewComments.Where(rc => rc.ItemPath == serverPath);
                        foreach (var item in tempFilteredList.GroupBy(p => p.Author).Select(group => new
                        {
                            Name = group.Key,
                            Count = group.Count()
                        })
                        .OrderByDescending(x => x.Count))
                        {
                            DictOfChangedFileDetails[serverPath].Item4.Add(item.Name + ": " + item.Count + " reviews(s)");

                            if (ListOfRecommendedReviewers.FirstOrDefault(f => f.Name == item.Name) == null)
                            {
                                ListOfRecommendedReviewers.Add(new RecommendedReviewers() { Name = item.Name, TotalNoOfCommits = 0, TotalNoOfReviews = item.Count });
                            }
                            else
                            {
                                ListOfRecommendedReviewers.FirstOrDefault(f => f.Name == item.Name).TotalNoOfReviews += 1;
                            }
                        }
                    }
                //}).ConfigureAwait(true);
                //}
            });

            //Populate list of recommendations
            List<string> lstRec = new List<string>();

            ListOfRecommendedReviewers.OrderByDescending(ord => ord.TotalNoOfCommits * 1 + ord.TotalNoOfReviews * 2).ToList().ForEach((itemLa) =>
            {
                lstRec.Add(itemLa.Name + ": " + itemLa.TotalNoOfCommits + " commit(s) & " + itemLa.TotalNoOfReviews + " review(s)");
            });

            ListOfRecommendations = lstRec;
        }

        ///// <summary>
        ///// Get/set the server path.
        ///// </summary>
        public string ServerPath
        {
            get { return m_serverPath; }
            set { m_serverPath = value; RaisePropertyChanged("ServerPath"); }
        }
        private string m_serverPath = String.Empty;



        /// <summary>
        /// Get/set the total no of changes.
        /// </summary>
        public int TotalNoOfChanges
        {
            get { return m_totalnoofchanges; }
            set { m_totalnoofchanges = value; RaisePropertyChanged("TotalNoOfChanges"); }
        }
        private int m_totalnoofchanges = 0;

        /// <summary>
        /// Get/set the List Of Commiters.
        /// </summary>
        public List<string> ListOfCommitters
        {
            get { return m_listofcommitters; }
            set { m_listofcommitters = value; RaisePropertyChanged("ListOfCommitters"); }
        }
        private List<string> m_listofcommitters = new List<string>();

        /// <summary>
        /// Get/set the List Of Reviewers.
        /// </summary>
        public List<string> ListOfReviewers
        {
            get { return m_listofreviewers; }
            set { m_listofreviewers = value; RaisePropertyChanged("ListOfReviewers"); }
        }
        private List<string> m_listofreviewers = new List<string>();

        /// <summary>
        /// Get/set the List Of RecommendedUsers.
        /// </summary>
        public List<string> ListOfRecommendations
        {
            get { return m_listofrecommendations; }
            set { m_listofrecommendations = value; RaisePropertyChanged("ListOfRecommendations"); }
        }
        private List<string> m_listofrecommendations = new List<string>();

    }

    public class CodeReviewComment
    {
        public CodeReviewComment()
        {

        }
        public CodeReviewComment(string author, string itemPath)
        {
            Author = author;
            ItemPath = itemPath;
        }
        public string Author { get; set; }
        //public string Comment { get; set; }
        //public string PublishDate { get; set; }
        public string ItemPath { get; set; }
    }


    public class RecommendedReviewers
    {
        public RecommendedReviewers()
        {

        }

        public string Name { get; set; }
        public int TotalNoOfCommits { get; set; }
        public int TotalNoOfReviews { get; set; }

    }
}
