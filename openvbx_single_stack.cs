cloudscript openvbx_single_stack
    version              = _latest
    result_template      = openvbx_result_template  

globals
    server_password	    = lib::random_password()
    console_password    = lib::random_password()
    openvbx_db_password = lib::random_password()
    openvbx_db_name     = 'OpenVBX'
    openvbx_db_username = 'openvbx'

thread openvbx_install
    tasks               = [config]
    
task config
    /key/password openvbx_server_key read_or_create
        key_group       = _SERVER
        password        = server_password
    
    /key/password openvbx_console_key read_or_create
        key_group       = _CONSOLE
        password        = console_password

    #
    # create openvbx storage slice, bootstrap script and recipe
    #
    
    # storage slice key
    /key/token openvbx_slice_key read_or_create
        username        = 'openvbxsliceuser'

    # slice
    /storage/slice openvbx_slice read_or_create
        keys            = [openvbx_slice_key]
    
    # slice container
    /storage/container openvbx_container => [openvbx_slice] read_or_create
        slice           = openvbx_slice
    
    # store script as object in cloudstorage
    /storage/object openvbx_install_script_object => [openvbx_slice, openvbx_container] read_or_create
        container_name  = 'openvbx_container'
        file_name       = 'install_openvbx.sh'
        slice           =  openvbx_slice
        content_data    =  install_openvbx_sh
        
    # associate the cloudstorage object with the openvbx script
    /orchestration/script openvbx_install_script => [openvbx_slice, openvbx_container, openvbx_install_script_object] read_or_create
        data_uri        = 'cloudstorage://openvbx_slice/openvbx_container/install_openvbx.sh'
        script_type     = _SHELL
        encoding        = _STORAGE
    
    # create the recipe and associate the script
    /orchestration/recipe openvbx_install_recipe read_or_create
        scripts         = [openvbx_install_script]

    # openvbx node
    /server/cloud openvbx_server read_or_create
        hostname        = 'openvbx'
        image           = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type    = 'CS05'
        keys            = [openvbx_server_key, openvbx_console_key]
        recipes         = [openvbx_install_recipe]

text_template openvbx_result_template

Thank you for provisioning an OpenVBX service.

You can now finish its configuration on the following page:

http://{{ openvbx_server.ipaddress_public }}/openvbx

Please use following credentials for Database Configuration:

hostname: localhost
username: {{ openvbx_db_username }}
password: {{ openvbx_db_password }}
database: {{ openvbx_db_name }}


You can also login to the server directly via SSH by connecting
to root@{{ openvbx_server.ipaddress_public }} using the password:

{{ openvbx_server_key.password }}

_eof


text_template install_openvbx_sh
#!/bin/bash
#
# install OpenVBX service
#

# check permissions
[ `whoami` = 'root' ] || {
    echo "ERROR: must have root permissions to execute the commands"
    exit 1
}

apt-get update > /dev/null
[ $? -eq 0 ] && echo "OK: update local apt cache" || {
    echo "ERROR: update local apt cache"
    exit 1
}

# install MySQL
DEBIAN_FRONTEND=noninteractive apt-get install -y mysql-server mysql-client > /dev/null
[ $? -eq 0 ] && echo "OK: install MySQL" || {
    echo "ERROR: install MySQL"
    exit 1
}

# install Apache2
DEBIAN_FRONTEND=noninteractive apt-get install -y apache2 > /dev/null
[ $? -eq 0 ] && echo "OK: install Apache2" || {
    echo "ERROR: install Apache2"
    exit 1
}

# install PHP5
apt-get install -y php5 php5-cli libapache2-mod-php5 > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5" || {
    echo "ERROR: install PHP5"
    exit 1
}
# install required PHP5 modules
apt-get install -y php5-mysql php5-memcache php5-curl php-apc > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5 modules" || {
    echo "ERROR: install PHP5 modules"
    exit 1
}
# enable PHP5 module
a2enmod php5 > /dev/null

# download OpenVBX source
cd /usr/local/src && wget -O openvbx.tgz https://github.com/twilio/OpenVBX/tarball/master > /dev/null 2>&1
[ $? -eq 0 ] && echo "OK: download OpenVBX source" || {
    echo "ERROR: download OpenVBX source"
    exit 1
}
# create OpenVBX target directory
mkdir -p /var/www/openvbx > /dev/null
[ $? -eq 0 ] && echo "OK: create OpenVBX target directory" || {
    echo "ERROR: create target directory"
    exit 1
}
# extract OpenVBX files
tar xzf /usr/local/src/openvbx.tgz -C /var/www/openvbx/ > /dev/null
[ $? -eq 0 ] && echo "OK: extract OpenVBX files" || {
    echo "ERROR: extract OpenVBX files"
    exit 1
}
# move OpenVBX files
cd /var/www/openvbx && mv ./* _ && mv ./_/* . && rm -rf _
[ $? -eq 0 ] && echo "OK: move OpenVBX files" || {
    echo "ERROR: move OpenVBX files"
    exit 1
}
# set ownership & permissions
cd /var/www/openvbx && chmod 0775 ./audio-uploads && chown www-data:www-data ./audio-uploads
cd /var/www/openvbx && chmod -R 0775 ./OpenVBX/config && chown -R www-data:www-data ./OpenVBX/config
[ $? -eq 0 ] && echo "OK: set required permissions" || {
    echo "ERROR: set required permissions"
    exit 1
}

# create MySQL database & user
mysql <<EOF
create database {{ openvbx_db_name }};
grant all on {{ openvbx_db_name }}.* to {{ openvbx_db_username }}@localhost identified by '{{ openvbx_db_password }}';
EOF
[ $? -eq 0 ] && echo "OK: create OpenVBX database and user" || {
    echo "ERROR: create OpenVBX database and user"
    exit 1
}

# enable mod_rewrite
a2enmod rewrite > /dev/null 2>&1

# create & update .htaccess
cd /var/www/openvbx && mv ./htaccess_dist ./.htaccess && sed 's@# RewriteBase /openvbx@RewriteBase /openvbx@g' -i ./.htaccess

# restart web server
/etc/init.d/apache2 stop  > /dev/null 2>&1
/etc/init.d/apache2 start > /dev/null 2>&1

echo "OK: install OpenVBX service"

_eof
