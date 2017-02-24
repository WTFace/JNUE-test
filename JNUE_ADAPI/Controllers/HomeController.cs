﻿using JNUE_ADAPI.Models;
using System.Web.Mvc;
using System.Linq;
using System;
using JNUE_ADAPI.AD;
using log4net;
using System.Reflection;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Generic;

namespace JNUE_ADAPI.Controllers
{
    public class HomeController : Controller
    {
        #region Private Fields
        readonly ILog logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        static StntNumbCheckViewModel _StntNumbModel = new StntNumbCheckViewModel();
        #endregion
        
        [HttpGet]
        public ActionResult Index()
        {
            AzureAD.getToken();
            return View();}

        [AcceptVerbs( HttpVerbs.Post | HttpVerbs.Patch)]
        public ActionResult Index(StntNumbCheckViewModel model)
        {
            if (ModelState.IsValid)
            {
                string oradb = "Data Source=(DESCRIPTION =(ADDRESS = (PROTOCOL = TCP)(HOST = 203.249.112.105)(PORT = 1521))(CONNECT_DATA =(SERVER = DEDICATED)(SERVICE_NAME = haksadb)));User Id=office; Password=office365;";
                using (OracleConnection conn = new OracleConnection(oradb))
                {
                    Dictionary<string, string> haksa = new Dictionary<string, string>();
                    try
                    {
                        conn.Open();
                        string sql = "select user_used,role,status,stnt_knam from office365 where stnt_numb= '" + model.Stnt_Numb.ToString() + "'";
                        OracleCommand cmd = new OracleCommand(sql, conn);
                        cmd.CommandType = System.Data.CommandType.Text;
                        OracleDataReader dr = cmd.ExecuteReader();
                        while (dr.Read())
                        {
                            haksa.Add("user_used", dr.GetString(0));
                            haksa.Add("role", dr.GetString(1));
                            haksa.Add("status", dr.GetString(2));
                            haksa.Add("stnt_knam", dr.GetString(3));
                        }conn.Close();
                        if (haksa.Count==0)
                        {
                            ModelState.AddModelError("", "입력하신 학번이 조회되지 않습니다.\n관리자에게 문의하여 주시기 바랍니다.");
                        }

                        if (haksa["user_used"] == "N") // 비활성화된 계정
                        { ModelState.AddModelError("", "입력하신 학번은 현재 사용중이지 않습니다.\n관리자에게 문의하여 주시기 바랍니다."); }

                        else if (LocalAD.ExistAttributeValue("extensionAttribute1", model.Stnt_Numb.ToString()) == true)
                        {
                            string upn = LocalAD.getSingleAttr("userPrincipalName", model.Stnt_Numb.ToString()); //@hddemo 포함
                            TempData["upn"] = upn; //login시 id 넘겨줄 용도

                            if (AzureAD.getUser(upn).Result.Equals("False"))
                            {
                                TempData["false"] = "가입은 되었으나 아직 계정이 생성되지 않았습니다.\n계정이 생성되면 로그인 화면으로 갈 수 있습니다.";
                                return RedirectToAction("Index", "Home");
                            }

                            AzureAD.setUsageLocation(upn);
                            if (haksa["status"] != LocalAD.getSingleAttr("description", model.Stnt_Numb.ToString())) //학적변동
                            {
                                LocalAD.UpdateStatus(model.Stnt_Numb.ToString(), haksa["status"]);
                                if (haksa["status"] == "2")
                                {
                                    TempData["status"] = "학적 상태가 '휴학'으로 변경되었습니다.";
                                }
                                else if (haksa["status"] == "1")
                                {
                                    TempData["status"] = "학적 상태가 '재학'으로 변경되었습니다.";
                                }
                                else { TempData["status"] = "학적 상태가 '졸업/퇴직'으로 변경되었습니다."; }

                                //License();
                                if (LocalAD.getSingleAttr("employeeType", model.Stnt_Numb.ToString()) == "student")
                                {
                                    if (haksa["status"] == "1")
                                    { //재
                                        var res = AzureAD.setLicense(upn, Properties.StuLicense, Properties.PlusLicense, Properties.disables);
                                    }
                                    else if (haksa["status"] == "2")
                                    { //휴
                                        var res = AzureAD.setLicense(upn, Properties.StuLicense, "", "");
                                        AzureAD.removeLicense(upn, Properties.PlusLicense);
                                    }
                                    else
                                    { //졸
                                        AzureAD.removeLicense(upn, "\"" + Properties.StuLicense + "\"" + "," + "\"" + Properties.PlusLicense + "\"");
                                    }
                                }
                                else if (LocalAD.getSingleAttr("employeeType", model.Stnt_Numb.ToString()) == "faculty")
                                {
                                    if (haksa["status"] == "0")
                                    { //퇴직
                                        AzureAD.removeLicense(upn, "\"" + Properties.FacLicense + "\"");
                                    }
                                    else
                                    {
                                        var res = AzureAD.setLicense(upn, Properties.FacLicense, "", ""); //재직
                                    }
                                }
                            }
                            return RedirectToAction("Alert", "Home");
                        }
                        else
                        {
                            _StntNumbModel = model;
                            // 없으면 회원가입페이지로 리디렉션
                            return RedirectToAction("RegisterJnueO365", "Home");
                        }
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", "학번 조회에 실패하였습니다.\n관리자에게 문의하여 주시기 바랍니다.");
                        logger.Debug(ex.ToString());
                    }
                }       
            }
            return View(model);
        }
        
        // GET: Home
        public ActionResult RegisterJnueO365()
        {
            return View();
        }

        public ActionResult Alert()
        {
            return View();
        }

        [HttpPost]
        public ActionResult RegisterJnueO365(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                string cua = LocalAD.CreateUserAccount(model.ID, model.Password, _StntNumbModel.Stnt_Numb.ToString());
                
                if (cua != "NONE")
                {
                    TempData["false"] = "가입은 되었으나 아직 계정이 생성되지 않았습니다.\n계정이 생성되면 로그인 화면으로 갈 수 있습니다.";
                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError("", "사용자를 추가할 수 없습니다.\n관리자에게 문의하여 주시기 바랍니다.");
            }
            return View(model);
        }
    }
}
