// Imports
const express = require('express');
const router = express.Router();
const pool = require('../app');
const { DateTime } = require('luxon');

const sharedData = {
  docContent: {}, // Use this object to store download links
};

module.exports = { sharedData };

/**
 * @route   GET /tenders/open?limit=&offset=&hasDocuments=
 * @desc    Get a list of all open tenders 
 * @access  Public
 * @param   {int} limit - Number of open tenders to be returned.
 * @param   {int} offset - Number of tenders to be skipped.
 * @param   {boolean} hasDocuments - If tenders to be returned have documents or not.
 */
router.get('/open', async (req, res, next) => {

  const { limit, offset, hasDocuments } = req.query;

  // Ensure limit and offset are valid numbers
  var limitNumber = parseInt(limit);
  var offsetNumber = parseInt(offset);

  //Default parameters if they are invalid
  if (isNaN(limitNumber))
    limitNumber = 200;
  if (isNaN(offsetNumber))
    offsetNumber = 0;

  try {

    // Validate query offset param
    const rowCountQuery = 'SELECT COUNT(*) FROM "tender_header"';
    const rowCountResult = await pool.query(rowCountQuery);
    const rowCount = parseInt(rowCountResult.rows[0].count, 10);

    // Throw error if offset surpasses number of rows in tender table
    if (offsetNumber >= rowCount) {
      const customError = new Error('Offset is out of range.');
      customError.status = 400;
      throw customError;
    }

    // Validate query hasDocuments param
    hasDocumentsBool = hasDocuments == "" ? false : hasDocuments !== undefined ? hasDocuments.toLowerCase() : undefined;

    const query = {
      text: `SELECT * FROM "tender_notice" WHERE closing_date > extract(epoch from now())${hasDocumentsBool === undefined ? '' : ' AND has_documents=$1'} ORDER BY pub_date DESC LIMIT $${hasDocumentsBool === undefined ? '1' : '2'} OFFSET $${hasDocumentsBool === undefined ? '2' : '3'}`,
      values: hasDocumentsBool === undefined ? [limitNumber, offsetNumber] : [hasDocumentsBool, limitNumber, offsetNumber],
    };

    // Format and return results
    const result = await pool.query(query);

    const formattedRows = result.rows.map(row => {

      var pubDate = row.pub_date
      var closingDate = row.closing_date

      // Format publication date from timestamp
      if (row.pub_date)
        pubDate = DateTime.fromSeconds(parseInt(row.pub_date.toFixed(0)));

      // Format closing date from timestamp
      if (row.closing_date)
        closingDate = DateTime.fromSeconds(parseInt(row.closing_date.toFixed(0)));

      return {
        id: row.nt_id,
        title: row.nt_title,
        category: row.proc_cat,
        buyer_org: row.buying_org,
        publication_date: pubDate,
        closing_date: closingDate,
        bid_type: row.nt_type,
        procurement_method: row.proc_method,
        selection_criteria: row.sel_criteria,
        link: row.ext_link && row.ext_link.length > 0 ? row.ext_link : row.nt_link,
        unspsc: row.unspsc,
        gsin: row.gsin,
      };

    });
    res.json(formattedRows);
  } catch (error) {
    next(error);
  }
});


/**
 * @route   GET /tenders/get-docs?nt_id=
 * @desc    Get a list of all open tenders 
 * @access  Public
 * @param   {string} nt_id - Notice id.
 */
router.get('/get-docs', async (req, res, next) => {

  const { nt_id } = req.query;

  try {
    //  -- Validate query parameters
    if (nt_id === undefined || nt_id == '') {
      const undefinedError = new Error('Notice ID is undefined');
      undefinedError.status = 400; // You can set the HTTP status code
      throw undefinedError;
    }

    //  -- Check if notice id exists in db table
    const noticeIdQuery = {
      text: 'SELECT * FROM "tender_notice" WHERE nt_id=$1',
      values: [String(nt_id)],
    };
    const noticeIdResult = await pool.query(noticeIdQuery);
    if (noticeIdResult.rows.length == 0) {
      const noticeExistsError = new Error(`No notice with ID "${nt_id}" is in the DB`);
      noticeExistsError.status = 400;
      throw noticeExistsError;
    }

    // -- Check if notice has documents
    else
      if (!noticeIdResult.rows[0].has_documents) {
        const noDocsError = new Error(`Notice with ID "${nt_id}" has no documents attached to it`);
        noDocsError.status = 400;
        throw noDocsError;
      }

    // -- Query docs list
    const docsQuery = {
      text: 'SELECT * FROM "tender_documents" WHERE nt_id=$1',
      values: [String(nt_id)],
    };
    const docsResult = await pool.query(docsQuery);

    // -- Format documents list
    const formattedRows = docsResult.rows.map((row) => {

      // -- Format publication date from timestamp
      var pubDate = row.pub_date ? DateTime.fromSeconds(parseInt(row.pub_date.toFixed(0))) : null;

      // -- Construct doc url
      sharedData.docContent[row.doc_title] = row.doc_content;
      const docUrl = `${req.protocol}://${req.get('host')}${req.baseUrl}/download-doc?doc_title=${encodeURIComponent(row.doc_title)}`;
      const documentLink = row.doc_content ?docUrl: row.doc_url;

      return {
        id: row.nt_id,
        title: row.doc_title,
        document: documentLink,
        language: row.doc_lang,
        type: row.doc_type,
        publication_date: pubDate
      };

    });
    
    res.json(formattedRows);
  } catch (error) {
    next(error);
  }
});

/**
 * @route   GET /tenders/download-doc?doc_title=
 * @desc    Get a list of document related to current notice
 * @access  Public
 * @param   {string} doc_title - Document title.
 */
router.get('/download-doc', async (req, res, next) => {
  const { doc_title } = req.query;

  try {

   // Validate document name
    if (doc_title === undefined || doc_title === '' ) {
      const undefinedError = new Error('Document title is undefined');
      undefinedError.status = 400;
      throw undefinedError;
    }

    // Validate doc binary content
    const binaryData = sharedData.docContent[doc_title];
    if (!binaryData) {
      const noContentError = new Error('Document content is not available');
      noContentError.status = 404;
      throw noContentError;
    }

    // Set the appropriate headers for binary data
    res.setHeader('Content-Type', 'application/octet-stream');
    res.setHeader('Content-Disposition', `inline; filename="${doc_title}"`);

    // Send the binary data in the response
    res.send(binaryData);
  } catch (error) {
    next(error);
  }
});

// Error-handling middleware
router.use((err, req, res, next) => {
  if (err.status) {
    // If the error has a status code, send it as a response
    res.status(err.status).json({ error: err.message });
  } else {
    console.log(err)
    // If no status code is specified, default to 500 (Internal Server Error)
    res.status(500).json({ error: 'Internal Server Error' });
  }
});
module.exports = router;