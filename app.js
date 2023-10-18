//Import dependencies
const express = require('express');
const { Pool } = require('pg');

//Init variables and export if necessary
const app = express();
const config = require('./config/config_dev.json');
const pool = new Pool(config.db_connection);
module.exports = pool;

// Router files
const tenderRoutes = require('./routes/tendersRoutes');

app.get('/', (req, res) => {
  res.json({ message: 'Welcome to the API!' });
});

// Mount the routers at specific paths
app.use('/tenders', tenderRoutes);


// Start the server
const port = process.env.PORT || 3000;
app.listen(port, () => {
  console.log(`Server is running on port ${port}`);
});