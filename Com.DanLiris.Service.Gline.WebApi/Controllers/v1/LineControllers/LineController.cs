﻿using AutoMapper;
using Com.DanLiris.Service.Gline.Lib.Interfaces;
using Com.DanLiris.Service.Gline.Lib.Models.MasterModel;
using Com.DanLiris.Service.Gline.Lib.Services;
using Com.DanLiris.Service.Gline.Lib.ViewModels.IntegrationViewModel;
using Com.DanLiris.Service.Gline.Lib.ViewModels.MasterViewModel;
using Com.DanLiris.Service.Gline.WebApi.Helpers;
using Com.Moonlay.NetCore.Lib.Service;
using CsvHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Com.DanLiris.Service.Gline.WebApi.Controllers.v1.LineControllers
{
    [Produces("application/json")]
    [ApiVersion("1.0")]
    [Route("v{version:apiVersion}/line")]
    [Authorize]
    public class LineController : Controller
    {
        private readonly string ApiVersion = "1.0.0";

        private readonly IServiceProvider serviceProvider;
        private readonly IdentityService identityService;
        private readonly IMapper mapper;
        private readonly ILineFacade facade;

        private readonly string ContentType = "application/vnd.openxmlformats";
        private readonly string FileName = string.Concat("Error Log - Upload Line - ", DateTime.Now.ToString("dd MMM yyyy"), ".csv");

        public LineController(IServiceProvider serviceProvider, ILineFacade facade, IMapper mapper)
        {
            this.serviceProvider = serviceProvider;
            this.mapper = mapper;
            this.facade = facade;
            identityService = (IdentityService)serviceProvider.GetService(typeof(IdentityService));
        }

        [HttpGet]
        public IActionResult Get(int page = 1, int size = 25, string order = "{}", string keyword = null, string filter = "{}")
        {
            try
            {
                var Data = facade.Read(page, size, order, keyword, filter);
                var newData = mapper.Map<List<LineViewModel>>(Data.Item1);

                List<object> listData = new List<object>();
                listData.AddRange(newData.AsQueryable().Select(s => new
                {
                    s.Id,
                    s.nama_line,
                    s.nama_gedung,
                    s.kode_unit,
                    s.nama_unit
                }));

                return Ok(new
                {
                    apiVersion = ApiVersion,
                    statusCode = General.OK_STATUS_CODE,
                    message = General.OK_MESSAGE,
                    data = listData,
                    info = new Dictionary<string, object>
                    {
                        { "count", listData.Count },
                        { "total", Data.Item2 },
                        { "order", Data.Item3 },
                        { "page", page },
                        { "size", size }
                    },
                });
            }
            catch (Exception e)
            {
                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.INTERNAL_ERROR_STATUS_CODE, e.Message)
                    .Fail();
                return StatusCode(General.INTERNAL_ERROR_STATUS_CODE, Result);
            }
        }

        [HttpGet("{id}")]
        public IActionResult Get(Guid id)
        {
            try
            {
                var result = facade.ReadById(id);
                LineViewModel viewModel = mapper.Map<LineViewModel>(result);
                if (viewModel == null)
                {
                    throw new Exception("Invalid Id");
                }

                return Ok(new
                {
                    apiVersion = ApiVersion,
                    statusCode = General.OK_STATUS_CODE,
                    message = General.OK_MESSAGE,
                    data = viewModel,
                });

            }
            catch (Exception e)
            {
                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.INTERNAL_ERROR_STATUS_CODE, e.Message)
                    .Fail();
                return StatusCode(General.INTERNAL_ERROR_STATUS_CODE, Result);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] LineViewModel viewModel)
        {
            identityService.Token = Request.Headers["Authorization"].First().Replace("Bearer ", "");
            identityService.Username = User.Claims.Single(p => p.Type.Equals("username")).Value;

            IValidateService validateService = (IValidateService)serviceProvider.GetService(typeof(IValidateService));

            try
            {
                viewModel.Id = Guid.NewGuid().ToString();
                validateService.Validate(viewModel);

                Line model = mapper.Map<Line>(viewModel);

                int result = await facade.Create(model, identityService.Username);

                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.CREATED_STATUS_CODE, General.OK_MESSAGE)
                    .Ok();
                return Created(String.Concat(Request.Path, "/", 0), Result);
            }
            catch (ServiceValidationExeption e)
            {
                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.BAD_REQUEST_STATUS_CODE, General.BAD_REQUEST_MESSAGE)
                    .Fail(e);
                return BadRequest(Result);
            }
            catch (Exception e)
            {
                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.INTERNAL_ERROR_STATUS_CODE, e.Message)
                    .Fail();
                return StatusCode(General.INTERNAL_ERROR_STATUS_CODE, Result);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put([FromRoute] Guid id, [FromBody] LineViewModel vm)
        {
            identityService.Username = User.Claims.Single(p => p.Type.Equals("username")).Value;

            Line m = mapper.Map<Line>(vm);

            IValidateService validateService = (IValidateService)serviceProvider.GetService(typeof(IValidateService));

            try
            {
                validateService.Validate(vm);

                int result = await facade.Update(id, m, identityService.Username);

                return NoContent();
            }
            catch (ServiceValidationExeption e)
            {
                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.BAD_REQUEST_STATUS_CODE, General.BAD_REQUEST_MESSAGE)
                    .Fail(e);
                return BadRequest(Result);

            }
            catch (Exception e)
            {
                Dictionary<string, object> Result =
                    new ResultFormatter(ApiVersion, General.INTERNAL_ERROR_STATUS_CODE, e.Message)
                    .Fail();
                return StatusCode(General.INTERNAL_ERROR_STATUS_CODE, Result);
            }

        }

        [HttpDelete("{id}")]
        public IActionResult Delete([FromRoute] Guid id)
        {
            identityService.Username = User.Claims.Single(p => p.Type.Equals("username")).Value;

            try
            {
                facade.Delete(id, identityService.Username);
                return NoContent();
            }
            catch (Exception)
            {
                return StatusCode(General.INTERNAL_ERROR_STATUS_CODE);
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> PostCSVFileAsync()
        {
            identityService.Username = User.Claims.Single(p => p.Type.Equals("username")).Value;

            try
            {
                if (Request.Form.Files.Count > 0)
                {
                    var UploadedFile = Request.Form.Files[0];
                    StreamReader Reader = new StreamReader(UploadedFile.OpenReadStream());
                    List<string> FileHeader = new List<string>(Reader.ReadLine().Replace("\"", string.Empty).Split(","));
                    var ValidHeader = facade.CsvHeader.SequenceEqual(FileHeader, StringComparer.OrdinalIgnoreCase);

                    if (ValidHeader)
                    {
                        Reader.DiscardBufferedData();
                        Reader.BaseStream.Seek(0, SeekOrigin.Begin);
                        Reader.BaseStream.Position = 0;
                        CsvReader Csv = new CsvReader(Reader);
                        Csv.Configuration.IgnoreQuotes = false;
                        Csv.Configuration.Delimiter = ",";
                        Csv.Configuration.RegisterClassMap<Lib.Facades.LineFacades.LineFacade.LineMap>();
                        Csv.Configuration.HeaderValidated = null;

                        List<LineCsvViewModel> viewModelCsv = Csv.GetRecords<LineCsvViewModel>().ToList();
                        Tuple<bool, List<object>> Validated = facade.UploadValidate(ref viewModelCsv, Request.Form.ToList());

                        Reader.Close();

                        if (Validated.Item1)
                        {

                            List<LineViewModel> viewModel = await facade.MapCsvToViewModel(viewModelCsv);
                            List<Line> model = mapper.Map<List<Line>>(viewModel);
                            await facade.UploadData(model, identityService.Username);

                            Dictionary<string, object> Result =
                               new ResultFormatter(ApiVersion, General.CREATED_STATUS_CODE, General.OK_MESSAGE)
                               .Ok();

                            return Created(HttpContext.Request.Path, Result);
                        }
                        else
                        {
                            using (MemoryStream memoryStream = new MemoryStream())
                            {
                                using (StreamWriter streamWriter = new StreamWriter(memoryStream))
                                {
                                    using (CsvWriter csvWriter = new CsvWriter(streamWriter))
                                    {
                                        csvWriter.WriteRecords(Validated.Item2);
                                    }

                                    return File(memoryStream.ToArray(), ContentType, FileName);
                                }
                            }
                        }
                    }
                    else
                    {
                        Dictionary<string, object> Result =
                          new ResultFormatter(ApiVersion, General.INTERNAL_ERROR_STATUS_CODE, General.CSV_ERROR_MESSAGE)
                          .Fail();

                        return NotFound(Result);
                    }
                }
                else
                {
                    Dictionary<string, object> Result =
                        new ResultFormatter(ApiVersion, General.BAD_REQUEST_STATUS_CODE, General.NO_FILE_ERROR_MESSAGE)
                            .Fail();
                    return BadRequest(Result);
                }
            }
            catch (Exception e)
            {
                Dictionary<string, object> Result =
                     new ResultFormatter(ApiVersion, General.INTERNAL_ERROR_STATUS_CODE, e.Message)
                     .Fail();
                return StatusCode(General.INTERNAL_ERROR_STATUS_CODE, Result);
            }
        }
    }
}
