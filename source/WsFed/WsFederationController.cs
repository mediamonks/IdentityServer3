﻿/*
 * Copyright (c) Dominick Baier, Brock Allen.  All rights reserved.
 * see license
 */
using System.IdentityModel.Services;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using Thinktecture.IdentityServer.Core;
using Thinktecture.IdentityServer.Core.Authentication;
using Thinktecture.IdentityServer.Core.Extensions;
using Thinktecture.IdentityServer.Core.Services;
using Thinktecture.IdentityServer.WsFed.ResponseHandling;
using Thinktecture.IdentityServer.WsFed.Results;
using Thinktecture.IdentityServer.WsFed.Services;
using Thinktecture.IdentityServer.WsFed.Validation;

namespace Thinktecture.IdentityServer.WsFed
{
    [HostAuthentication("idsrv")]
    public class WsFederationController : ApiController
    {
        private readonly ICoreSettings _settings;
        private readonly IUserService _users;

        private ILogger _logger;
        private SignInValidator _validator;
        private SignInResponseGenerator _signInResponseGenerator;
        private MetadataResponseGenerator _metadataResponseGenerator;


        public WsFederationController(ICoreSettings settings, IUserService users, ILogger logger)
        {
            _settings = settings;
            _users = users;
            _logger = logger;

            // todo: DI
            _validator = new SignInValidator(logger);
            _signInResponseGenerator = new SignInResponseGenerator(logger, settings);
            _metadataResponseGenerator = new MetadataResponseGenerator(logger, settings);
        }

        [Route("wsfed")]
        public async Task<IHttpActionResult> Get()
        {
            WSFederationMessage message;
            if (WSFederationMessage.TryCreateFromUri(Request.RequestUri, out message))
            {
                var signin = message as SignInRequestMessage;
                if (signin != null)
                {
                    return await ProcessSignInAsync(signin);
                }

                var signout = message as SignOutRequestMessage;
                if (signout != null)
                {
                    // todo: call main signout page which in turn calls back the ws-fed specific one 
                    var ctx = Request.GetOwinContext();
                    ctx.Authentication.SignOut(Constants.PrimaryAuthenticationType);
                    
                    return await SignOutCallback();
                }
            }

            return BadRequest("Invalid WS-Federation request");
        }

        [Route("wsfed/signout")]
        [HttpGet]
        public async Task<IHttpActionResult> SignOutCallback()
        {
            var cookies = new CookieMiddlewareCookieService(Request.GetOwinContext());
            var urls = await cookies.GetValuesAndDeleteCookieAsync();

            return new SignOutResult(urls);
        }

        [Route("wsfed/metadata")]
        public IHttpActionResult GetMetadata()
        {
            var ep = Request.GetBaseUrl(_settings.GetPublicHost()) + "wsfed";
            var entity = _metadataResponseGenerator.Generate(ep);

            return new MetadataResult(entity);
        }

        private async Task<IHttpActionResult> ProcessSignInAsync(SignInRequestMessage msg)
        {
            var result = _validator.Validate(msg, User as ClaimsPrincipal);

            if (result.IsSignInRequired)
            {
                return RedirectToLogin(_settings);
            }
            if (result.IsError)
            {
                return BadRequest(result.Error);
            }

            var responseMessage = _signInResponseGenerator.GenerateResponse(result);

            // todo: DI
            var cookies = new CookieMiddlewareCookieService(Request.GetOwinContext());
            await cookies.AddValueAsync(result.ReplyUrl);

            return new SignInResult(responseMessage);
        }

        IHttpActionResult RedirectToLogin(ICoreSettings settings)
        {
            var message = new SignInMessage();
            message.ReturnUrl = Request.RequestUri.AbsoluteUri;

            return new LoginResult(message, this.Request, settings);
        }
    }
}