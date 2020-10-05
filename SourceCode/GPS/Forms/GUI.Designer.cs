﻿using AgOpenGPS.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        //ABLines directory
        public string ablinesDirectory;

        //colors for sections and field background
        public byte flagColor = 0;

        //how many cm off line per big pixel
        public int lightbarCmPerPixel, decimals = 0;

        //polygon mode for section drawing
        public bool isDrawPolygons;

        //Is it in 2D or 3D, metric or imperial, display lightbar, display grid etc
        public bool isMetric = true, isLightbarOn = true, isGridOn, isFullScreen, TwoSecondUpdateBool, OneSecondUpdateBool;
        public bool isUTurnAlwaysOn, isAutoLoadFields, isCompassOn, isSpeedoOn, isAutoDayNight, isSideGuideLines = true;
        public bool isPureDisplayOn = true, DrawBackBuffer = false, isSkyOn = true, isRollMeterOn = false;
        public bool isDay = true, isDayTime = true, isSimNoisy, isKeyboardOn = true;

        public double metImp2m = 0.01, m2MetImp = 100.0, cutoffMetricImperial = 1;

        //master Manual and Auto, 3 states possible
        public enum btnStates { Off, Auto, On }
        public btnStates autoBtnState = btnStates.Off;

        public Color dayColor = Properties.Settings.Default.setDisplay_colorDayMode;
        public Color nightColor = Properties.Settings.Default.setDisplay_colorNightMode;
        public Color sectionColorDay = Properties.Settings.Default.setDisplay_colorSectionsDay;
        public Color sectionColorNight = Properties.Settings.Default.setDisplay_colorSectionsNight;
        public Color fieldColorDay = Properties.Settings.Default.setDisplay_colorFieldDay;
        public Color fieldColorNight = Properties.Settings.Default.setDisplay_colorFieldNight;

        public int[] customColorsList = new int[16];

        //sunrise sunset
        public DateTime dateToday = DateTime.Today, sunrise = DateTime.Now, sunset = DateTime.Now;

        private void IsBetweenSunriseSunset(double lat, double lon)
        {
            CSunTimes.Instance.CalculateSunRiseSetTimes(pn.latitude, pn.longitude, dateToday, ref sunrise, ref sunset);
            isDay = (DateTime.Now.Ticks < sunset.Ticks && DateTime.Now.Ticks > sunrise.Ticks);
        }

        private void LoadSettings()
        {
            //metric settings
            if (isMetric = Settings.Default.setMenu_isMetric)
            {
                metImp2m = 0.01;
                m2MetImp = 100.0;
                cutoffMetricImperial = 1;
                decimals = 0;
            }
            else
            {
                metImp2m = Glm.in2m;
                m2MetImp = Glm.m2in;
                cutoffMetricImperial = 1.60934;
                decimals = 3;
            }




            if (Settings.Default.setF_workingDirectory == "Default")
                baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\AgOpenGPS\\";
            else baseDirectory = Settings.Default.setF_workingDirectory + "\\AgOpenGPS\\";

            //get the fields directory, if not exist, create
            fieldsDirectory = baseDirectory + "Fields\\";
            string dir = Path.GetDirectoryName(fieldsDirectory);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            //get the fields directory, if not exist, create
            vehiclesDirectory = baseDirectory + "Vehicles\\";
            dir = Path.GetDirectoryName(vehiclesDirectory);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            //get the Tools directory, if not exist, create
            toolsDirectory = baseDirectory + "Tools\\";
            dir = Path.GetDirectoryName(toolsDirectory);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            //get the Tools directory, if not exist, create
            envDirectory = baseDirectory + "Environments\\";
            dir = Path.GetDirectoryName(envDirectory);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            //make sure current field directory exists, null if not
            currentFieldDirectory = Settings.Default.setF_CurrentDir;

            string curDir;
            if (currentFieldDirectory != "")
            {
                curDir = fieldsDirectory + currentFieldDirectory + "//";
                dir = Path.GetDirectoryName(curDir);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    currentFieldDirectory = "";
                    Settings.Default.setF_CurrentDir = "";
                    Settings.Default.Save();
                }
            }

            string directoryName = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string wave = Path.Combine(directoryName, "Dependencies\\Audio", "Boundary.Wav");
            if (File.Exists(wave))
            {
                SndBoundaryAlarm = new SoundPlayer(wave);
            }
            else
            {
                SndBoundaryAlarm = new SoundPlayer(Resources.Alarm10);
            }

            //grab the current vehicle filename - make sure it exists
            envFileName = Vehicle.Default.setVehicle_envName;

            timerSim.Interval = 94;

            fixUpdateTime = 1.0 / (double)HzTime;

            //get the abLines directory, if not exist, create
            ablinesDirectory = baseDirectory + "ABLines\\";
            dir = Path.GetDirectoryName(fieldsDirectory);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            //set baud and port from last time run
            baudRateGPS = Settings.Default.setPort_baudRate;
            portNameGPS = Settings.Default.setPort_portNameGPS;

            if (Settings.Default.setMenu_isSimulatorOn)
            {
                simulatorOnToolStripMenuItem.Checked = true;
                panelSim.Visible = true;
                timerSim.Enabled = true;
            }
            else
            {
                //try and open
                SerialPortOpenGPS();
            }

            //same for SectionMachine port
            portNameMachine = Settings.Default.setPort_portNameMachine;
            wasRateMachineConnectedLastRun = Settings.Default.setPort_wasMachineConnected;
            if (wasRateMachineConnectedLastRun) SerialPortMachineOpen();

            //same for AutoSteer port
            portNameAutoSteer = Settings.Default.setPort_portNameAutoSteer;
            wasAutoSteerConnectedLastRun = Settings.Default.setPort_wasAutoSteerConnected;
            if (wasAutoSteerConnectedLastRun) SerialPortAutoSteerOpen();


            //set the correct zoom and grid
            camera.camSetDistance = camera.zoomValue * camera.zoomValue * -1;
            SetZoom();

            //which heading source is being used
            headingFromSource = Settings.Default.setGPS_headingFromWhichSource;

            //start udp server if required
            if (Settings.Default.setUDP_isInterAppOn) StartLocalUDPServer();
            else if (Settings.Default.setUDP_isOn) StartUDPServer();
            else StopUDPServer();

            //start NTRIP if required
            isNTRIP_TurnedOn = Settings.Default.setNTRIP_isOn;


            //workswitch stuff
            mc.isWorkSwitchEnabled = Vehicle.Default.setF_IsWorkSwitchEnabled;
            mc.isWorkSwitchActiveLow = Vehicle.Default.setF_IsWorkSwitchActiveLow;
            mc.isWorkSwitchManual = Vehicle.Default.setF_IsWorkSwitchManual;
            mc.RemoteAutoSteer = Vehicle.Default.setAS_isAutoSteerAutoOn;

            minFixStepDist = Settings.Default.setF_minFixStep;

            fd.workedAreaTotalUser = Settings.Default.setF_UserTotalArea;
            fd.userSquareMetersAlarm = Settings.Default.setF_UserTripAlarm;

            //space between points while recording a boundary
            //boundaryTriggerDistance = Settings.Default.setF_boundaryTriggerDistance;

            //load the last used auto turn shape
            string fileAndDir = @".\Dependencies\YouTurnShapes\" + Settings.Default.setAS_youTurnShape;
            yt.LoadYouTurnShapeFromFile(fileAndDir);

            //sim.latitude = Settings.Default.setSim_lastLat;
            //sim.longitude = Settings.Default.setSim_lastLong;

            //load th elightbar resolution
            lightbarCmPerPixel = Settings.Default.setDisplay_lightbarCmPerPixel;

            //Stanley guidance
            isStanleyUsed = Vehicle.Default.setVehicle_isStanleyUsed;

            isRTK = Settings.Default.setGPS_isRTK;




            isSkyOn = Settings.Default.setMenu_isSkyOn;
            isGridOn = Settings.Default.setMenu_isGridOn;
            isCompassOn = Settings.Default.setMenu_isCompassOn;
            isSpeedoOn = Settings.Default.setMenu_isSpeedoOn;
            isAutoDayNight = Settings.Default.setDisplay_isAutoDayNight;
            isSideGuideLines = Settings.Default.setMenu_isSideGuideLines;
            isLogNMEA = Settings.Default.setMenu_isLogNMEA;
            isPureDisplayOn = Settings.Default.setMenu_isPureOn;
            isUTurnAlwaysOn = Settings.Default.setMenu_isUTurnAlwaysOn;
            isAutoLoadFields = Settings.Default.AutoLoadFields;
            DrawBackBuffer = Settings.Default.DrawBackBuffer;
            //set the language to last used
            SetLanguage((object)Settings.Default.setF_culture, null);


            simulatorOnToolStripMenuItem.Checked = Settings.Default.setMenu_isSimulatorOn;
            if (simulatorOnToolStripMenuItem.Checked)
            {
                panelSim.Visible = true;
                timerSim.Enabled = true;
            }
            else
            {
                panelSim.Visible = false;
                timerSim.Enabled = false;
            }

            //set the flag mark button to red dot
            btnFlag.Image = Properties.Resources.FlagRed;

            //night mode
            //isDay = Properties.Settings.Default.setDisplay_isDayMode;
            isDay = !isDay;
            SwapDayNightMode();

            //load the string of custom colors
            string[] words = Properties.Settings.Default.setDisplay_customColors.Split(',');
            for (int i = 0; i < 16; i++)
            {
                customColorsList[i] = int.Parse(words[i], CultureInfo.InvariantCulture);
            }

            //load up colors
            fieldColorDay = Settings.Default.setDisplay_colorFieldDay;
            sectionColorDay = Settings.Default.setDisplay_colorSectionsDay;
            fieldColorNight = Settings.Default.setDisplay_colorFieldNight;
            sectionColorNight = Settings.Default.setDisplay_colorSectionsNight;
            dayColor = Settings.Default.setDisplay_colorDayMode;
            nightColor = Settings.Default.setDisplay_colorNightMode;


            YouTurnButtons(false);

            isLightbarOn = Settings.Default.setMenu_isLightbarOn;
            lightbarToolStripMenuItem.Checked = isLightbarOn;

            //set up grid and lightbar

            isKeyboardOn = Settings.Default.setDisplay_isKeyboardOn;
            keyboardToolStripMenuItem1.Checked = isKeyboardOn;


            if (Settings.Default.setMenu_isOGLZoomOn == 1)
                topFieldViewToolStripMenuItem.Checked = true;
            else topFieldViewToolStripMenuItem.Checked = false;

            oglZoom.Width = 400;
            oglZoom.Height = 400;
            oglZoom.Visible = true;
            oglZoom.Left = 300;
            oglZoom.Top = 80;

            oglZoom.SendToBack();


            yt.rowSkipsWidth = Properties.Vehicle.Default.set_youSkipWidth;
            cboxpRowWidth.SelectedIndex = yt.rowSkipsWidth - 1;

            FixPanelsAndMenus();

            UpdateNtripButton();

            stripSectionColor.BackColor = sectionColorDay;

            if (isAutoLoadFields)
            {
                LoadFields();
            }
        }

        public void LoadFields()
        {
            Fields.Clear();

            string[] dirs = Directory.GetDirectories(fieldsDirectory);
            foreach (string dir in dirs)
            {
                double northingOffset = 0;
                double eastingOffset = 0;
                double convergenceAngle = 0;
                string fieldDirectory = Path.GetFileName(dir);
                string filename = dir + "\\Field.txt";
                string line;

                //make sure directory has a field.txt in it
                if (File.Exists(filename))
                {
                    using (StreamReader reader = new StreamReader(filename))
                    {
                        try
                        {
                            //Date time line
                            for (int i = 0; i < 4; i++)
                            {
                                line = reader.ReadLine();
                            }

                            //start positions
                            if (!reader.EndOfStream)
                            {
                                line = reader.ReadLine();
                                string[] offs = line.Split(',');

                                eastingOffset = (double.Parse(offs[0], CultureInfo.InvariantCulture));
                                northingOffset = (double.Parse(offs[1], CultureInfo.InvariantCulture));
                                line = reader.ReadLine();
                                if (!reader.EndOfStream)
                                {
                                    line = reader.ReadLine();
                                    convergenceAngle = double.Parse(line, CultureInfo.InvariantCulture);
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }

                    //grab the boundary
                    filename = dir + "\\Boundary.txt";

                    if (File.Exists(filename))
                    {
                        using (StreamReader reader = new StreamReader(filename))
                        {
                            try
                            {
                                //read header
                                line = reader.ReadLine();//Boundary

                                if (!reader.EndOfStream) //empty boundary field
                                {
                                    //True or False OR points from older boundary files
                                    line = reader.ReadLine();


                                    //Check for older boundary files, then above line string is num of points
                                    if (line == "True" || line == "False")
                                    {
                                        line = reader.ReadLine(); //number of points
                                        line = reader.ReadLine(); //number of points
                                    }

                                    int numPoints = int.Parse(line);
                                    if (numPoints > 0)
                                    {
                                        Fields.Add(new CAutoLoadField());
                                        Fields[Fields.Count - 1].Dir = Path.GetFileName(dir);

                                        //load the line
                                        for (int i = 0; i < numPoints; i++)
                                        {
                                            line = reader.ReadLine();
                                            string[] words2 = line.Split(',');
                                            double easting = double.Parse(words2[0], CultureInfo.InvariantCulture);
                                            double northing = double.Parse(words2[1], CultureInfo.InvariantCulture);
                                            Vec2 vecPt = new Vec2((Math.Sin(convergenceAngle) * easting) + (Math.Cos(convergenceAngle) * northing) + northingOffset, (Math.Cos(convergenceAngle) * easting) - (Math.Sin(convergenceAngle) * northing) + eastingOffset);

                                            if (i == 0)
                                            {
                                                Fields[Fields.Count - 1].Northingmin = Fields[Fields.Count - 1].Northingmax = vecPt.Northing;
                                                Fields[Fields.Count - 1].Eastingmin = Fields[Fields.Count - 1].Eastingmax = vecPt.Easting;
                                            }

                                            if (Fields[Fields.Count - 1].Northingmin > vecPt.Northing) Fields[Fields.Count - 1].Northingmin = vecPt.Northing;
                                            if (Fields[Fields.Count - 1].Northingmax < vecPt.Northing) Fields[Fields.Count - 1].Northingmax = vecPt.Northing;
                                            if (Fields[Fields.Count - 1].Eastingmin > vecPt.Easting) Fields[Fields.Count - 1].Eastingmin = vecPt.Easting;
                                            if (Fields[Fields.Count - 1].Eastingmax < vecPt.Easting) Fields[Fields.Count - 1].Eastingmax = vecPt.Easting;

                                            Fields[Fields.Count - 1].Boundary.Add(vecPt);
                                        }
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
        }

        public void SwapDayNightMode()
        {
            isDay = !isDay;
            if (isDay)
            {
                btnDayNightMode.Image = Properties.Resources.WindowNightMode;

                this.BackColor = dayColor;
                foreach (Control c in this.Controls)
                {
                    //if (c is Label || c is Button)
                    {
                        c.ForeColor = Color.Black;
                    }
                }
            }
            else //nightmode
            {
                btnDayNightMode.Image = Properties.Resources.WindowDayMode;

                this.BackColor = nightColor;

                foreach (Control c in this.Controls)
                {
                    {
                        c.ForeColor = Color.White;
                    }
                }
            }
            LineUpManualBtns();
            Properties.Settings.Default.setDisplay_isDayMode = isDay;
            Properties.Settings.Default.Save();
        }

        private void FixPanelsAndMenus()
        {
            oglMain.Left = statusStripLeft.Width;
            if (Settings.Default.setDisplay_isSimple)
            {
                oglMain.Width = Width - 17 - statusStripLeft.Width - layoutPanelRight.Width / 2;
            }
            else
            {
                oglMain.Width = Width - 16 - statusStripLeft.Width - layoutPanelRight.Width;
            }

            if (isFullScreen) oglMain.Width += 16;

            if (Settings.Default.setDisplay_isSimple)
            {
                toolToolbottomStripBtn.Visible = false;
                vehicleToolStripBtn.Visible = false;
                AutoSteerToolBtn.Visible = false;

                lblDateTime.Visible = true;
                snapLeftBigStrip.Visible = true;
                snapRightBigStrip.Visible = true;
            }
            else
            {
                toolToolbottomStripBtn.Visible = true;
                vehicleToolStripBtn.Visible = true;
                AutoSteerToolBtn.Visible = true;

                if (Width > 1100)
                {
                    snapLeftBigStrip.Visible = true;
                    snapRightBigStrip.Visible = true;
                }
                else
                {
                    snapLeftBigStrip.Visible = false;
                    snapRightBigStrip.Visible = false;
                }
                if (Width > 1300)
                {
                    lblDateTime.Visible = true;
                }
                else
                {
                    lblDateTime.Visible = false;
                }
            }
        }

        public string FindDirection(double heading)
        {
            if (heading < 0) heading += Glm.twoPI;

            heading = Glm.ToDegrees(heading);

            if (heading > 337.5 || heading < 22.5)
            {
                return (" " +  gStr.gsNorth + " ");
            }
            if (heading > 22.5 && heading < 67.5)
            {
                return (" " +  gStr.gsN_East + " ");
            }
            if (heading > 67.5 && heading < 111.5)
            {
                return (" " +  gStr.gsEast + " ");
            }
            if (heading > 111.5 && heading < 157.5)
            {
                return (" " +  gStr.gsS_East + " ");
            }
            if (heading > 157.5 && heading < 202.5)
            {
                return (" " +  gStr.gsSouth + " ");
            }
            if (heading > 202.5 && heading < 247.5)
            {
                return (" " +  gStr.gsS_West + " ");
            }
            if (heading > 247.5 && heading < 292.5)
            {
                return (" " +  gStr.gsWest + " ");
            }
            if (heading > 292.5 && heading < 337.5)
            {
                return (" " +  gStr.gsN_West + " ");
            }
            return (" " +  gStr.gsLost + " ");
        }

        //line up section On Off Auto buttons based on how many there are
        public void LineUpManualBtns()
        {
            int oglCenter = statusStripLeft.Width + oglMain.Width / 2;

            int top = 180;
            if (panelSim.Visible == true) top = 230;

            panelSim.Width = Math.Min(panelSim.MaximumSize.Width, oglMain.Width - 10);
            panelSim.Left = oglCenter - panelSim.Width / 2;


            for (int i = 0; i < Tools.Count; i++)
            {
                if (Tools[i].numOfSections > 0)
                {
                    Size Size = new System.Drawing.Size(Math.Min((oglMain.Width * 3 / 4) / Tools[i].numOfSections, 120), 30);

                    for (int j = 0; j < Tools[i].Sections.Count; j++)
                    {
                        if (j < Tools[i].numOfSections)
                        {
                            Tools[i].Sections[j].SectionButton.Top = Height - top - 30 * i;
                            Tools[i].Sections[j].SectionButton.Size = Size;
                            Tools[i].Sections[j].SectionButton.Left = (oglCenter) - (Tools[i].numOfSections * Size.Width) / 2 + Size.Width * j;
                            Tools[i].Sections[j].SectionButton.Visible = true;
                        }
                        else Tools[i].Sections[j].SectionButton.Visible = false;
                    }
                }
            }
        }

        //Mouse Clicks 
        private void oglMain_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                //0 at bottom for opengl, 0 at top for windows, so invert Y value
                Point point = oglMain.PointToClient(Cursor.Position);

                //label3.Text = point.X.ToString();

                if (point.Y < 140 && point.Y > 40)
                {
                    int middle = oglMain.Width / 2 + oglMain.Width / 5;
                    if (point.X > middle - 80 && point.X < middle + 80)
                    {
                        SwapDirection();
                        return;
                    }

                    middle = oglMain.Width / 2 - oglMain.Width / 4;
                    if (point.X > middle - 140 && point.X < middle)
                    {
                        if (yt.isYouTurnTriggered)
                        {
                            yt.ResetYouTurn();
                            CurveLines.OldHowManyPathsAway = double.NegativeInfinity;
                        }
                        else
                        {
                            yt.isYouTurnTriggered = true;
                            yt.BuildManualYouTurn(false, true);
                            return;
                        }
                    }

                    if (point.X > middle && point.X < middle + 140)
                    {
                        if (yt.isYouTurnTriggered)
                        {
                            yt.ResetYouTurn();
                            CurveLines.OldHowManyPathsAway = double.NegativeInfinity;
                        }
                        else
                        {
                            yt.isYouTurnTriggered = true;
                            yt.BuildManualYouTurn(true, true);
                            return;
                        }
                    }
                }

                //prevent flag selection if flag form is up
                Form fc = Application.OpenForms["FormFlags"];
                if (fc != null)
                {
                    fc.Focus();
                    return;
                }

                mouseX = point.X;
                mouseY = oglMain.Height - point.Y;
                LeftMouseDownOnOpenGL = true;
            }
        }

        private void oglZoom_MouseClick(object sender, MouseEventArgs e)
        {
            if ((sender as Control).IsDragging()) return;

            if (oglZoom.Width == 180)
            {
                oglZoom.Width = 300;
                oglZoom.Height = 300;
            }

            else if (oglZoom.Width == 300)
            {
                oglZoom.Width = 180;
                oglZoom.Height = 180;
            }
        }               

        //Function to delete flag
        public void DeleteSelectedFlag()
        {
            //delete selected flag and set selected to none
            flagPts.RemoveAt(flagNumberPicked - 1);
            flagNumberPicked = 0;

            // re-sort the id's based on how many flags left
            int flagCnt = flagPts.Count;
            if (flagCnt > 0)
            {
                for (int i = 0; i < flagCnt; i++) flagPts[i].ID = i + 1;
            }
        }

        public void YouTurnButtons(bool Enable)
        {
            yt.ResetYouTurn();
            yt.isYouTurnBtnOn = false;
            btnAutoYouTurn.Enabled = Enable;
            btnAutoYouTurn.Image = Properties.Resources.YouTurnNo;
        }

        private void ShowNoGPSWarning()
        {
            oglMain.MakeCurrent();
            oglMain.Refresh();
        }

        private void HalfSecond_Update(object sender, EventArgs e)
        {
            HalfSecondUpdate.Enabled = false;

            AutoSteerToolBtn.Text = SetSteerAngle + "\r\n" + ActualSteerAngle;


            lblSpeed.Text = Math.Round(avgSpeed / cutoffMetricImperial, 1).ToString();


            if (isMetric)  //metric or imperial
            {
                btnContour.Text = (crossTrackError / 10 + gStr.gsCM); //cross track error
            }
            else  //Imperial Measurements
            {
                btnContour.Text = ((int)(crossTrackError / 25.54) + " in"); //cross track errorss
            }

            lblHz.Text = Math.Round(HzTime, 1, MidpointRounding.AwayFromZero) + " Hz " + (int)(FrameTime) + "\r\n" + FixQuality;

            if (OneSecondUpdateBool = !OneSecondUpdateBool)
            {
                //counter used for saving field in background
                MinuteCounter++;

                //Make sure it is off when it should
                if ((!ABLines.BtnABLineOn && !ct.isContourBtnOn && !CurveLines.BtnCurveLineOn && isAutoSteerBtnOn)
                    || (recPath.isDrivingRecordedPath && isAutoSteerBtnOn))
                {
                    isAutoSteerBtnOn = false;
                    btnAutoSteer.Image = Properties.Resources.AutoSteerOff;
                    //if (yt.isYouTurnBtnOn) btnAutoYouTurn.PerformClick();
                }

                //do all the NTRIP routines
                if (isNTRIP_TurnedOn)
                {
                    //increment once every second
                    NtripCounter++;

                    //Thinks is connected but not receiving anything // 30sec maybe a bit much?
                    if (NTRIP_Watchdog++ > 10 && isNTRIP_Connected) ReconnectRequest();

                    //Once all connected set the timer GGA to NTRIP Settings
                    if (sendGGAInterval > 0 && NtripCounter == 40) tmr.Interval = sendGGAInterval * 1000;

                    //Have we connection
                    if (!isNTRIP_Connected && !isNTRIP_Connecting)
                    {
                        if (NtripCounter > 20) StartNTRIP();
                    }
                    else if (isNTRIP_Connecting)
                    {
                        if (NtripCounter > 25)//give it 5 seconds
                        {
                            TimedMessageBox(2000, gStr.gsSocketConnectionProblem, gStr.gsNotConnectingToCaster);
                            ReconnectRequest();
                        }
                        if (clientSocket != null && clientSocket.Connected)
                        {
                            SendAuthorization();
                        }
                    }

                    //update byte counter and up counter
                    if (NtripCounter > 20) NTRIPStartStopStrip.Text = (isNTRIP_Connecting ? gStr.gsAuthourizing : isNTRIP_Sending ? gStr.gsSendingGGA : (NTRIP_Watchdog > 10 ? gStr.gsWaiting : gStr.gsListening)) + "\n" + string.Format("{0:00}:{1:00}", ((NtripCounter - 21) / 60), (Math.Abs(NtripCounter - 21)) % 60);
                    else NTRIPStartStopStrip.Text = gStr.gsConnectingIn + "\n" + (Math.Abs(NtripCounter - 21));

                    pbarNtripMenu.Value = unchecked((byte)(tripBytes * 0.02));
                    NTRIPBytesMenu.Text = ((tripBytes) * 0.001).ToString("###,###,###") + " kb";

                    if (sendGGAInterval > 0 && isNTRIP_Sending)
                    {
                        isNTRIP_Sending = false;
                    }
                }

                //the main formgps window
                //status strip values
                if (isMetric)
                {
                    distanceToolBtn.Text = fd.DistanceUserMeters + "\r\n" + fd.WorkedUserHectares;
                    fieldStatusStripText.Text = fd.WorkedAreaRemainHectares + "\r\n" +
                                                   fd.WorkedAreaRemainPercentage + "\r\n" +
                                                   fd.TimeTillFinished + "\r\n" +
                                                   fd.WorkRateHectares;
                }
                else
                {
                    distanceToolBtn.Text = fd.DistanceUserFeet + "\r\n" + fd.WorkedUserAcres;
                    fieldStatusStripText.Text = fd.WorkedAreaRemainAcres + "\r\n" +
                           fd.WorkedAreaRemainPercentage + "\r\n" +
                           fd.TimeTillFinished + "\r\n" +
                           fd.WorkRateAcres;
                }

                //statusbar flash red undefined headland
                if (mc.isOutOfBounds && statusStripBottom.BackColor == Color.Transparent) statusStripBottom.BackColor = Color.Tomato;
                else if (!mc.isOutOfBounds && statusStripBottom.BackColor == Color.Tomato) statusStripBottom.BackColor = Color.Transparent;

                //check to make sure the grid is on the right place
                worldGrid.CheckWorldGrid(pn.fix.Northing, pn.fix.Easting);

                //not Metric/Standard units sensitive
                if (ABLines.BtnABLineOn) btnABLine.Text = "# " + ABLines.HowManyPathsAway.ToString();
                else btnABLine.Text = "";

                if (CurveLines.BtnCurveLineOn) btnCurve.Text = "# " + CurveLines.HowManyPathsAway.ToString();
                else btnCurve.Text = "";

                lblDateTime.Text = DateTime.Now.ToString("HH:mm:ss") + "\n\r" + DateTime.Now.ToString("ddd MMM yyyy");

                zoomUpdateCounter = TwoSecondUpdateBool = !TwoSecondUpdateBool;
            }

            HalfSecondUpdate.Enabled = true;
        }

        #region Properties // ---------------------------------------------------------------------


        public string FixQuality
        {
            get
            {
                if (pn.FixQuality == 0) return "Invalid ";
                else if (pn.FixQuality == 1) return "GPS Single ";
                else if (pn.FixQuality == 2) return "DGPS ";
                else if (pn.FixQuality == 3) return "PPS ";
                else if (pn.FixQuality == 4) return "RTK Fix ";
                else if (pn.FixQuality == 5) return "Float ";
                else if (pn.FixQuality == 6) return "Estimate ";
                else if (pn.FixQuality == 7) return "Man IP ";
                else if (pn.FixQuality == 8) return "Sim ";
                else return "Unknown: ";
            }
        }
        public string GyroInDegrees
        {
            get
            {
                if (ahrs.correctionHeadingX16 != 9999)
                    return Math.Round(ahrs.correctionHeadingX16 * 0.0625, 1) + "\u00B0";
                else return "-";
            }
        }
        public string RollInDegrees
        {
            get
            {
                if (ahrs.isRollFromAutoSteer || ahrs.isRollFromAVR)
                    return Math.Round((ahrs.rollX16 - ahrs.rollZeroX16) * 0.0625, 1) + "\u00B0";
                else return "-";
            }
        }
        public string SetSteerAngle { get { return ((double)(guidanceLineSteerAngle) * 0.01).ToString("N2") + "\u00B0"; } }
        public string ActualSteerAngle { get { return ((double)(actualSteerAngleDisp) * 0.01).ToString("N2") + "\u00B0"; } }
        public string Altitude { get { return Convert.ToString(Math.Round(pn.altitude,1)); } }
        public string AltitudeFeet { get { return Convert.ToString((Math.Round((pn.altitude * 3.28084),1))); } }
        public string DistPivotM
        {
            get
            {
                if (distancePivotToTurnLine > 0 )
                    return ((int)(distancePivotToTurnLine)) + " m";
                else return "--";
            }
        }
        public string DistPivotFt
        {
            get
            {
                if (distancePivotToTurnLine > 0 ) return (((int)(Glm.m2ft * (distancePivotToTurnLine))) + " ft");
                else return "--";
            }
        }
        #endregion properties 
    }//end class
}//end namespace