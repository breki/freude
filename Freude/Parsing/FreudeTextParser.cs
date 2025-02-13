﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;
using System.Text.RegularExpressions;
using Freude.DocModel;

namespace Freude.Parsing
{
    public class FreudeTextParser : IFreudeTextParser
    {
        public FreudeTextParser(IWikiTextTokenizer tokenizer)
        {
            Contract.Requires(tokenizer != null);
            this.tokenizer = tokenizer;
        }

        public DocumentDef ParseText(string text, out ParsingContext context)
        {
            DocumentDefBuilder docBuilder = new DocumentDefBuilder();

            context = new ParsingContext(text.SplitIntoLines());

            while (!context.EndOfText)
            {
                if (!ParseLine(docBuilder, context))
                    break;
            }

            docBuilder.FinalizeCurrentParagraph();

            return docBuilder.Document;
        }

        private bool ParseLine(DocumentDefBuilder docBuilder, ParsingContext context)
        {
            Contract.Requires (docBuilder != null);

            if (context.EndOfText)
                return false;

            string lineText = context.CurrentLine;

            // we can ignore lines with nothing but whitespace
            if (lineText.Length == 0 || lineText.Trim ().Length == 0)
                docBuilder.FinalizeCurrentParagraph();
            else
                ProcessLine (docBuilder, context, lineText);

            context.IncrementLineCounter ();
            return true;
        }

        private void ProcessLine(
            DocumentDefBuilder docBuilder, 
            ParsingContext context,
            string lineText)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(lineText != null);

            WikiTokenizationSettings tokenizationSettings = new WikiTokenizationSettings();
            tokenizationSettings.IsWholeLine = true;
            IList<WikiTextToken> tokens = tokenizer.TokenizeWikiText(lineText, tokenizationSettings);

            if (tokens.Count == 0)
                throw new NotImplementedException("todo next:");

            TokenBuffer tokenBuffer = new TokenBuffer(tokens);

            WikiTextToken firstToken = tokenBuffer.Token;

            switch (firstToken.Type)
            {
                case WikiTextToken.TokenType.Heading1Start:
                case WikiTextToken.TokenType.Heading2Start:
                case WikiTextToken.TokenType.Heading3Start:
                case WikiTextToken.TokenType.Heading4Start:
                case WikiTextToken.TokenType.Heading5Start:
                case WikiTextToken.TokenType.Heading6Start:
                    docBuilder.FinalizeCurrentParagraph();
                    HandleHeadingLine(docBuilder, context, tokenBuffer);
                    break;

                case WikiTextToken.TokenType.Text:
                case WikiTextToken.TokenType.DoubleApostrophe:
                case WikiTextToken.TokenType.TripleApostrophe:
                case WikiTextToken.TokenType.DoubleSquareBracketsOpen:
                case WikiTextToken.TokenType.SingleSquareBracketsOpen:
                case WikiTextToken.TokenType.BulletList:
                case WikiTextToken.TokenType.NumberedList:
                case WikiTextToken.TokenType.Indent:
                    HandleText (docBuilder, context, tokenBuffer);
                    break;

                default:
                    throw new NotImplementedException("todo next: {0}".Fmt(firstToken.Type));
            }
        }

        private static void HandleHeadingLine (DocumentDefBuilder docBuilder, ParsingContext context, TokenBuffer tokenBuffer)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(tokenBuffer != null);

            WikiTextToken firstToken = tokenBuffer.Token;

            WikiTextToken.TokenType endingTokenNeeded;
            int headingLevel;
            switch (firstToken.Type)
            {
                case WikiTextToken.TokenType.Heading1Start:
                    headingLevel = 1;
                    endingTokenNeeded = WikiTextToken.TokenType.Heading1End;
                    break;
                case WikiTextToken.TokenType.Heading2Start:
                    headingLevel = 2;
                    endingTokenNeeded = WikiTextToken.TokenType.Heading2End;
                    break;
                case WikiTextToken.TokenType.Heading3Start:
                    headingLevel = 3;
                    endingTokenNeeded = WikiTextToken.TokenType.Heading3End;
                    break;
                case WikiTextToken.TokenType.Heading4Start:
                    headingLevel = 4;
                    endingTokenNeeded = WikiTextToken.TokenType.Heading4End;
                    break;
                case WikiTextToken.TokenType.Heading5Start:
                    headingLevel = 5;
                    endingTokenNeeded = WikiTextToken.TokenType.Heading5End;
                    break;
                case WikiTextToken.TokenType.Heading6Start:
                    headingLevel = 6;
                    endingTokenNeeded = WikiTextToken.TokenType.Heading6End;
                    break;

                default:
                    throw new InvalidOperationException("BUG");
            }

            tokenBuffer.MoveToNextToken();

            StringBuilder headingText = new StringBuilder();
            if (!ProcessHeadingText(context, tokenBuffer, endingTokenNeeded, headingText))
                return;

            HeadingElement headingEl = new HeadingElement (headingText.ToString ().Trim (), headingLevel);
            docBuilder.AddRootChild(headingEl);

            tokenBuffer.MoveToNextToken();
            string anchorId;
            if (!ProcessHeadingAnchor (context, tokenBuffer, out anchorId))
                return;

            tokenBuffer.ProcessUntilEnd (
                t =>
                {
                    context.ReportError("Unexpected token at the end of heading");
                    return false;
                });

            headingEl.AnchorId = anchorId;
        }

        private static void HandleText(
            DocumentDefBuilder docBuilder, 
            ParsingContext context, 
            TokenBuffer tokenBuffer)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(tokenBuffer != null);

            TextParsingMode parsingMode = TextParsingMode.RegularText;
            InternalLinkIdBuilder internalLinkBuilder = new InternalLinkIdBuilder ();
            StringBuilder textBuilder = null;
            Uri externalLinkUrl = null;
            bool isFirstTextElementOfContinuedLine = true;

            bool processingSuccessful = tokenBuffer.ProcessUntilEnd (
                t =>
                {
                    bool flagCopy = isFirstTextElementOfContinuedLine;

                    switch (t.Type)
                    {
                        case WikiTextToken.TokenType.BulletList:
                            return HandleBulletListTokenInText(docBuilder, t);

                        case WikiTextToken.TokenType.NumberedList:
                            return HandleNumberedListTokenInText(docBuilder, t);

                        case WikiTextToken.TokenType.Indent:
                            return HandleIndentTokenInText(docBuilder, t);

                        case WikiTextToken.TokenType.Text:
                        {
                            isFirstTextElementOfContinuedLine = false;
                            return HandleTextTokenInText(
                                docBuilder, parsingMode, t, flagCopy, internalLinkBuilder, ref textBuilder);
                        }

                        case WikiTextToken.TokenType.TripleApostrophe:
                            return HandleTripleApostropheTokenInText(docBuilder);

                        case WikiTextToken.TokenType.DoubleApostrophe:
                            return HandleDoubleApostropheTokenInText (docBuilder);

                        case WikiTextToken.TokenType.DoubleSquareBracketsOpen:
                            return HandleDoubleSquareBracketsOpenTokenInText(context, t, ref parsingMode);

                        case WikiTextToken.TokenType.NamespaceSeparator:
                            return HandleNamespaceSeparatorTokenInText (context, parsingMode, internalLinkBuilder, t);

                        case WikiTextToken.TokenType.Pipe:
                            return HandlePipeTokenInText(context, t, ref parsingMode);

                        case WikiTextToken.TokenType.DoubleSquareBracketsClose:
                            return HandleDoubleSquareBracketsCloseTokenInText(
                                docBuilder, 
                                context, 
                                ref parsingMode, 
                                t,
                                internalLinkBuilder, 
                                textBuilder);

                        case WikiTextToken.TokenType.SingleSquareBracketsOpen:
                            return HandleSingleSquareBracketsOpenTokenInText (context, t, ref parsingMode);

                        case WikiTextToken.TokenType.ExternalLinkUrlLeadingSpace:
                            return HandleExternalLinkUrlLeadingSpaceTokenInText(context, t, parsingMode);

                        case WikiTextToken.TokenType.ExternalLinkUrl:
                            return HandleExternalLinkUrlTokenInText(context, t, ref parsingMode, ref externalLinkUrl);

                        case WikiTextToken.TokenType.SingleSquareBracketsClose:
                            return HandleSingleSquareBracketsCloseTokenInText (
                                docBuilder,
                                context,
                                ref parsingMode,
                                t,
                                externalLinkUrl,
                                textBuilder);

                        default:
                            throw new NotImplementedException("todo next: {0}".Fmt(t.Type));
                    }
                });

            if (processingSuccessful)
            {
                switch (parsingMode)
                {
                    case TextParsingMode.InternalLinkPageName:
                    case TextParsingMode.InternalLinkDescription:
                        context.ReportError ("Missing token ']]'");
                        break;
                    case TextParsingMode.ExternalLinkUrl:
                    case TextParsingMode.ExternalLinkDescription:
                        context.ReportError ("Missing token ']'");
                        break;
                }
            }
        }

        private static bool HandleBulletListTokenInText(DocumentDefBuilder docBuilder, WikiTextToken token)
        {
            Contract.Requires(docBuilder != null);
            docBuilder.StartNewParagraph(ParagraphElement.ParagraphType.Bulleted, token.Text.Length - 1);
            return true;
        }

        private static bool HandleNumberedListTokenInText(DocumentDefBuilder docBuilder, WikiTextToken token)
        {
            Contract.Requires(docBuilder != null);
            docBuilder.StartNewParagraph(ParagraphElement.ParagraphType.Numbered, token.Text.Length - 1);
            return true;
        }

        private static bool HandleIndentTokenInText(DocumentDefBuilder docBuilder, WikiTextToken token)
        {
            Contract.Requires(docBuilder != null);
            docBuilder.StartNewParagraph(ParagraphElement.ParagraphType.Regular, token.Text.Length);
            return true;
        }

        private static bool HandleTextTokenInText(
            DocumentDefBuilder docBuilder, 
            TextParsingMode parsingMode, 
            WikiTextToken token,
            bool isFirstElementOfContinuedLine,
            InternalLinkIdBuilder linkIdBuilder, 
            ref StringBuilder textBuilder)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(token != null);
            Contract.Requires(linkIdBuilder != null);

            switch (parsingMode)
            {
                case TextParsingMode.RegularText:
                    docBuilder.AddTextToParagraph(token.Text, isFirstElementOfContinuedLine);
                    return true;
                case TextParsingMode.InternalLinkPageName:
                    linkIdBuilder.AppendText(token.Text);
                    return true;
                case TextParsingMode.InternalLinkDescription:
                    if (textBuilder == null)
                        textBuilder = new StringBuilder();
                    textBuilder.Append(token.Text);
                    return true;
                case TextParsingMode.ExternalLinkDescription:
                    if (textBuilder == null)
                        textBuilder = new StringBuilder();
                    textBuilder.Append(token.Text);
                    return true;
                default:
                    throw new NotImplementedException("todo next:");
            }
        }

        private static bool HandleTripleApostropheTokenInText(DocumentDefBuilder docBuilder)
        {
            switch (docBuilder.CurrentParagraphTextStyle)
            {
                case null:
                case TextElement.TextStyle.Regular:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.Bold;
                    break;
                case TextElement.TextStyle.Bold:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.Regular;
                    break;
                case TextElement.TextStyle.Italic:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.BoldItalic;
                    break;
                case TextElement.TextStyle.BoldItalic:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.Italic;
                    break;
            }

            return true;
        }

        private static bool HandleDoubleApostropheTokenInText(DocumentDefBuilder docBuilder)
        {
            switch (docBuilder.CurrentParagraphTextStyle)
            {
                case null:
                case TextElement.TextStyle.Regular:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.Italic;
                    break;
                case TextElement.TextStyle.Italic:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.Regular;
                    break;
                case TextElement.TextStyle.Bold:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.BoldItalic;
                    break;
                case TextElement.TextStyle.BoldItalic:
                    docBuilder.CurrentParagraphTextStyle = TextElement.TextStyle.Bold;
                    break;
            }

            return true;
        }

        private static bool HandleDoubleSquareBracketsOpenTokenInText(
            ParsingContext context, 
            WikiTextToken token,
            ref TextParsingMode parsingMode)
        {
            Contract.Requires(context != null);
            Contract.Requires(token != null);

            if (parsingMode != TextParsingMode.RegularText)
            {
                context.ReportError("Token {0} is not allowed here".Fmt(token.Text));
                return false;
            }

            parsingMode = TextParsingMode.InternalLinkPageName;
            return true;
        }

        private static bool HandleNamespaceSeparatorTokenInText(
            ParsingContext context, TextParsingMode parsingMode, InternalLinkIdBuilder linkIdBuilder, WikiTextToken token)
        {
            Contract.Requires(context != null);
            Contract.Requires(linkIdBuilder != null);
            Contract.Requires(token != null);

            if (parsingMode != TextParsingMode.InternalLinkPageName)
            {
                context.ReportError ("Token {0} is not allowed here".Fmt (token.Text));
                return false;
            }

            linkIdBuilder.AddSeparator();

            return true;
        }

        private static bool HandlePipeTokenInText(ParsingContext context, WikiTextToken token, ref TextParsingMode parsingMode)
        {
            Contract.Requires(context != null);
            Contract.Requires(token != null);

            if (parsingMode != TextParsingMode.InternalLinkPageName)
            {
                context.ReportError("Token {0} is not allowed here".Fmt(token.Text));
                return false;
            }

            parsingMode = TextParsingMode.InternalLinkDescription;
            return true;
        }

        private static bool HandleDoubleSquareBracketsCloseTokenInText(
            DocumentDefBuilder docBuilder, 
            ParsingContext context,
            ref TextParsingMode parsingMode, 
            WikiTextToken token, 
            InternalLinkIdBuilder linkIdBuilder,
            StringBuilder internalLinkDescription)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(token != null);
            Contract.Requires(linkIdBuilder != null);

            if (parsingMode != TextParsingMode.InternalLinkPageName
                && parsingMode != TextParsingMode.InternalLinkDescription)
            {
                context.ReportError("Token {0} is not allowed here".Fmt(token.Text));
                return false;
            }

            parsingMode = TextParsingMode.RegularText;
            bool result = AddInternalLink(docBuilder, context, linkIdBuilder, internalLinkDescription);

            linkIdBuilder.Clear();

            if (internalLinkDescription != null)
                internalLinkDescription.Clear();

            return result;
        }

        private static bool HandleSingleSquareBracketsOpenTokenInText (
            ParsingContext context, WikiTextToken token, ref TextParsingMode parsingMode)
        {
            Contract.Requires (context != null);
            Contract.Requires (token != null);

            if (parsingMode != TextParsingMode.RegularText)
            {
                context.ReportError ("Token {0} is not allowed here".Fmt (token.Text));
                return false;
            }

            parsingMode = TextParsingMode.ExternalLinkUrl;
            return true;
        }

        private static bool HandleExternalLinkUrlLeadingSpaceTokenInText (
            ParsingContext context, 
            WikiTextToken token, 
            TextParsingMode parsingMode)
        {
            if (parsingMode != TextParsingMode.ExternalLinkUrl)
            {
                context.ReportError ("Token {0} is not allowed here".Fmt (token.Text));
                return false;
            }

            return true;
        }

        private static bool HandleExternalLinkUrlTokenInText (
            ParsingContext context, 
            WikiTextToken token, 
            ref TextParsingMode parsingMode, 
            ref Uri externalLinkUrl)
        {
            if (parsingMode != TextParsingMode.ExternalLinkUrl)
            {
                context.ReportError ("Token {0} is not allowed here".Fmt (token.Text));
                return false;
            }

            string uriString = token.Text.Trim ();

            try
            {
                externalLinkUrl = new Uri(uriString);
            }
            catch (UriFormatException ex)
            {
                context.ReportError ("Invalid external link URL '{0}': {1}".Fmt(uriString, ex.Message));
                return false;
            }

            parsingMode = TextParsingMode.ExternalLinkDescription;

            return true;
        }

        private static bool HandleSingleSquareBracketsCloseTokenInText (
            DocumentDefBuilder docBuilder, 
            ParsingContext context, 
            ref TextParsingMode parsingMode, 
            WikiTextToken token, 
            Uri externalLinkUrl, 
            StringBuilder textBuilder)
        {
            if (parsingMode != TextParsingMode.ExternalLinkUrl
                && parsingMode != TextParsingMode.ExternalLinkDescription)
            {
                context.ReportError ("Token {0} is not allowed here".Fmt (token.Text));
                return false;
            }

            parsingMode = TextParsingMode.RegularText;
            return AddExternalLink (docBuilder, externalLinkUrl, textBuilder);
        }

        private static bool AddInternalLink(
            DocumentDefBuilder docBuilder, 
            ParsingContext context,
            InternalLinkIdBuilder linkIdBuilder, 
            StringBuilder internalLinkDescription)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(linkIdBuilder != null);

            InternalLinkId linkId = linkIdBuilder.Build (context);

            if (linkId == null)
                return false;

            string description = null;
            if (internalLinkDescription != null)
            {
                description = internalLinkDescription.ToString ().Trim ();
                if (description.Length == 0)
                {
                    context.ReportError("Internal link ('{0}') has an empty description".Fmt(linkId));
                    return false;
                }
            }

            InternalLinkElement linkEl = new InternalLinkElement(linkId, description);
            docBuilder.AddToParagraph(linkEl);

            return true;
        }

        private static bool AddExternalLink(
            DocumentDefBuilder docBuilder, 
            Uri externalLinkUrl, 
            StringBuilder externalLinkDescription)
        {
            Contract.Requires(docBuilder != null);
            Contract.Requires(externalLinkUrl != null);

            string description = null;
            if (externalLinkDescription != null)
            {
                description = externalLinkDescription.ToString ().Trim ();
                if (description.Length == 0)
                    description = null;
            }

            ExternalLinkElement linkEl = new ExternalLinkElement (externalLinkUrl, description);
            docBuilder.AddToParagraph(linkEl);

            return true;
        }

        private static bool ProcessHeadingText(
            ParsingContext context, 
            TokenBuffer tokenBuffer, 
            WikiTextToken.TokenType endingTokenNeeded, 
            StringBuilder headingText)
        {
            Contract.Requires(context != null);
            Contract.Requires(tokenBuffer != null);
            Contract.Requires(headingText != null);

            WikiTextToken.TokenType? actualEndingToken = tokenBuffer.ProcessUntilToken (
                context, 
                t =>
                {
                    switch (t.Type)
                    {
                        case WikiTextToken.TokenType.Text:
                            headingText.Append(t.Text);
                            return true;
                        case WikiTextToken.TokenType.DoubleApostrophe:
                        case WikiTextToken.TokenType.TripleApostrophe:
                            throw new NotImplementedException("todo next: add support");
                        default:
                            context.ReportError("Unexpected token in heading definition: {0}".Fmt(t.Text));
                            return false;
                    }
                }, 
                endingTokenNeeded);

            return actualEndingToken != null && actualEndingToken == endingTokenNeeded;
        }

        private static bool ProcessHeadingAnchor(ParsingContext context, TokenBuffer tokenBuffer, out string anchorId)
        {
            Contract.Requires(context != null);
            Contract.Requires(tokenBuffer != null);

            anchorId = null;
            if (!tokenBuffer.EndOfTokens)
            {
                WikiTextToken token = tokenBuffer.Token;
                if (token.Type == WikiTextToken.TokenType.HeadingAnchor)
                {
                    tokenBuffer.MoveToNextToken();
                    if (!FetchHeadingAnchor(context, tokenBuffer, out anchorId))
                        return false;
                }
                else
                {
                    context.ReportError("Unexpected tokens after ending heading token");
                    return false;
                }
            }

            return true;
        }

        private static bool FetchHeadingAnchor(ParsingContext context, TokenBuffer tokenBuffer, out string anchorId)
        {
            Contract.Requires(context != null);
            Contract.Requires(tokenBuffer != null);

            anchorId = null;

            WikiTextToken token = tokenBuffer.ExpectToken(context, WikiTextToken.TokenType.Text);
            if (token == null)
                return false;

            string potentialAnchorId = token.Text;
            if (!ValidateAnchor(potentialAnchorId))
            {
                context.ReportError("Invalid heading anchor ID: '{0}'".Fmt(potentialAnchorId));
                return false;
            }

            anchorId = token.Text;
            tokenBuffer.MoveToNextToken();
            return true;
        }

        private static bool ValidateAnchor(string anchorId)
        {
            Contract.Requires(anchorId != null);

            if (anchorId.Length == 0)
                return false;

            return anchorRegex.IsMatch(anchorId);
        }

        private readonly IWikiTextTokenizer tokenizer;
        private static readonly Regex anchorRegex = new Regex(@"^[\d\w\-\._~!\$\&`\(\)\*\+\,\;\=\:\@]+$", RegexOptions.Compiled);

        private enum TextParsingMode
        {
            RegularText,
            InternalLinkPageName,
            InternalLinkDescription,
            ExternalLinkUrl,
            ExternalLinkDescription,
        }
    }
}